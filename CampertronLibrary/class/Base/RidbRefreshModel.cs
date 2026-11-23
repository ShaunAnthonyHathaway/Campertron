using System.Collections.ObjectModel;

// The host-neutral RIDB refresh service's request, options, progress and result (V011-1, decision D61;
// business-rules.md §8). RidbRefreshService.Run downloads the RIDB export, builds and validates a staging database and
// installs it (RidbStagedRefresh), then records the refresh in General.yaml. The console's "Refresh Ridb Data" and the
// start's automatic refresh reach it through RefreshRidbRecreationData.RefreshData, which writes the lines of Log and
// keeps the console's messages and exit behavior; the GUI's Settings > System > Campground data runs it on a worker thread
// with progress and cancellation. Nothing here holds rendered text for a user: the GUI words every outcome itself and
// never shows Problems, Detail or an exception's message (they can name paths).

// what to refresh: the settings folder (with RecreationDotOrg.db and General.yaml) and the cache folder it clears.
// InContainer: Campertron runs in its container (CampertronInternalConfig.ContainerMode: /config and /cache, D9). The
// refresh's download folder and its extraction then go into the cache folder, the mounted /cache (D85 point 5, v0.3.0's
// PR 9), instead of LocalApplicationData, which in the container is the container's own storage (/app, open question #27).
// Elsewhere they stay in LocalApplicationData. The staging database stays in the settings folder in either case (D26)
internal sealed record RidbRefreshRequest(string ConfigPath, string CachePath, bool InContainer = false);

// the steps of a refresh, in the order they run
internal enum RidbRefreshStage
{
    // taking the refresh lock in the settings folder, or finding that another refresh holds it
    AcquiringLock,
    // removing what an interrupted refresh left: staging folders, General.yaml's temporary files, download folders
    CleaningUp,
    // creating this refresh's own download folder
    Preparing,
    // downloading the export (bytes; the total when the server gives it)
    Downloading,
    // extracting the export's files (ZIP entries)
    Extracting,
    // a staging copy of the installed database, its imported tables emptied
    PreparingDatabase,
    // reading the six RIDB files (files)
    Importing,
    // inserting the rows in one transaction
    Saving,
    // Campertron's index of the campsite attributes (D51)
    Indexing,
    // RidbDatabase.Validate on the staging database
    Validating,
    // writing the staging database's WAL into its file
    Finalizing,
    // from here on the refresh can't be canceled: the derived cache is cleared before the installation
    ClearingCache,
    // the online backup into the installed database (one write transaction)
    Installing,
    // quick_check on the installed database
    CheckingDatabase,
    // lastRidbDataRefresh in General.yaml
    RecordingRefresh,
    Completed
}

// how a refresh ended
internal enum RidbRefreshOutcome
{
    // installed, quick_check ok, General.yaml updated
    Refreshed,
    // installed and quick_check ok, but General.yaml could not be updated (a warning: the next start may refresh again)
    RefreshedWithoutTimestamp,
    // canceled before the cache was cleared: nothing installed or changed, the refresh's own files removed
    Canceled,
    // another refresh holds the lock (this process or another one): nothing was changed
    AnotherRefreshRunning,
    // the download failed (connection, HTTP status, the transfer): nothing was changed
    DownloadFailed,
    // nothing arrived for the stall timeout (or the server did not answer within the header timeout): nothing was changed
    DownloadTimedOut,
    // the export is not a readable ZIP archive: nothing was changed
    ExtractionFailed,
    // the export's files could not be imported (malformed JSON, rejected rows): nothing was changed
    ImportFailed,
    // the staging database failed RidbDatabase.Validate (Problems): nothing was changed
    ValidationFailed,
    // a file or folder could not be read, written or cleared (full disk, permissions, an unusable cache folder):
    // the installed database was not changed
    StorageFailed,
    // the installation itself failed (busy, read-only, full disk): the installed database is unchanged (atomic backup)
    InstallationFailed,
    // installed, but quick_check did not return ok (Problems); the staging database was kept; General.yaml not updated
    InstalledButCheckFailed,
    // anything else (Error has its type); Installed says whether the new data was installed before it
    Unexpected
}

// One step of a refresh. Sequence grows with every report. CanCancel is false from ClearingCache on. Done and Total count
// what the stage counts (Downloading: bytes, Total null when the server sends no length; Extracting: ZIP entries;
// Importing: the RIDB files); both null for the other stages.
internal sealed record RidbRefreshProgress(long Sequence, RidbRefreshStage Stage, bool CanCancel, long? Done, long? Total);

internal sealed class RidbRefreshResult
{
    internal RidbRefreshResult(RidbRefreshOutcome Outcome, RidbRefreshStage Stage, bool Installed, DateTime? RefreshedAt, TimeSpan Elapsed,
        IReadOnlyList<string> Problems, string? Detail, Exception? Error, bool CancelRequested)
    {
        this.Outcome = Outcome;
        this.Stage = Stage;
        this.Installed = Installed;
        this.RefreshedAt = RefreshedAt;
        this.Elapsed = Elapsed;
        this.Problems = Problems;
        this.Detail = Detail;
        this.Error = Error;
        this.CancelRequested = CancelRequested;
    }
    internal RidbRefreshOutcome Outcome { get; }
    // the stage the refresh ended in (Completed after a success)
    internal RidbRefreshStage Stage { get; }
    // the installed database holds the refreshed data
    internal bool Installed { get; }
    // the refresh's time (UTC) when the data was installed: the value written (or meant) for lastRidbDataRefresh
    internal DateTime? RefreshedAt { get; }
    internal TimeSpan Elapsed { get; }
    // RidbDatabase.Validate's problems, or quick_check's; the console prints them (they can name paths)
    internal IReadOnlyList<string> Problems { get; }
    // the lock's or General.yaml's reason, for the console (it can name a path)
    internal string? Detail { get; }
    // the exception that ended the refresh (the console rethrows it, as before the service); null for Refreshed,
    // AnotherRefreshRunning, ValidationFailed and InstalledButCheckFailed
    internal Exception? Error { get; }
    // the token was canceled at some point, also after the refresh could no longer stop
    internal bool CancelRequested { get; }
    internal bool Succeeded => Outcome == RidbRefreshOutcome.Refreshed || Outcome == RidbRefreshOutcome.RefreshedWithoutTimestamp;
    internal string? ErrorType => Error?.GetType().Name;

    internal static readonly IReadOnlyList<string> NoProblems = new ReadOnlyCollection<string>(Array.Empty<string>());
}

// The bounds and seams of a refresh. The defaults are the production values; tests replace the transport, the folders
// and the timeouts.
internal sealed class RidbRefreshOptions
{
    internal Uri ExportUri { get; init; } = new Uri(CampertronLibrary.function.RecDotOrg.api.RidbRefreshService.ExportUrl);
    // null: a new HttpClient for the download (the system's proxy settings, as before)
    internal HttpMessageHandler? Handler { get; init; }
    // how long the server may take to answer with its headers; null: HttpClient's default (100 seconds), as before
    internal TimeSpan? HeaderTimeout { get; init; }
    // the download ends with DownloadTimedOut when nothing arrives for this long (before V011-1 a stalled body hung)
    internal TimeSpan StallTimeout { get; init; } = CampertronLibrary.function.RecDotOrg.api.RidbRefreshService.DefaultStallTimeout;
    // where the download folder and CampertronExtract go; null: the request's own (RidbRefreshService.DefaultWorkRoot:
    // the cache folder in the container, D85; LocalApplicationData elsewhere, as before)
    internal string? WorkRoot { get; init; }
    // replaces the download and the extraction: returns a folder with the six RIDB files (tests; the console overload of
    // RefreshData). The service never removes that folder; the import deletes it after a success, as before
    internal Func<CancellationToken, string>? ObtainExtractDirectory { get; init; }
    // the console's lines (RefreshData passes Console.WriteLine); null: nothing is written
    internal Action<string>? Log { get; init; }
    internal Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;
    internal TimeProvider Time { get; init; } = TimeProvider.System;
}
