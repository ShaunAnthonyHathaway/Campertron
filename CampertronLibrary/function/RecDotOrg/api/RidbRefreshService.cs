using System.Diagnostics;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.sqlite;
using Microsoft.Data.Sqlite;

namespace CampertronLibrary.function.RecDotOrg.api
{
    // The host-neutral RIDB refresh (V011-1, decision D61; business-rules.md §8), in the pattern of GS1's search service:
    // a request with explicit folders, typed progress, a cancellation token and a typed result. It writes nothing to the
    // console (only to Options.Log, which the console's RefreshRidbRecreationData.RefreshData gives Console.WriteLine),
    // never waits for a key and never ends the process. The steps and their order are the failure-safe refresh of D26:
    // the refresh lock, the leftovers of an interrupted refresh, the download into a folder of this refresh's own, the
    // extraction, the staging database (copy, import, index, validation, checkpoint), the cache, the installation by
    // SQLite's online backup under the repair lock, quick_check, and lastRidbDataRefresh in General.yaml while the lock is
    // still held. Until the cache step the token cancels it at the next checkpoint (before the download, while it downloads,
    // between and within ZIP entries, between the RIDB files, between the steps); a canceled or failed refresh removes its
    // download folder, its extraction and its staging folder and leaves the installed database, the cache and General.yaml
    // as they were. From the cache step on the token is no longer read: the refresh installs, checks and records to its
    // end. Run throws only for a request it can't start (ArgumentException, or Clear.CheckCacheDirectory's
    // InvalidOperationException for an unusable cache folder), before anything is locked, downloaded or changed.
    internal static class RidbRefreshService
    {
        internal const string ExportUrl = "https://ridb.recreation.gov/downloads/RIDBFullExport_V1_JSON.zip";
        internal const string ArchiveName = "RIDBFullExport_V1_JSON.zip";
        // this refresh's own download folder: the prefix and 32 lowercase hexadecimal digits (a new GUID), never a name
        // another program would use (TD-58: the archive used to be LocalApplicationData/RIDBFullExport_V1_JSON.zip, and a
        // file of that name was deleted)
        internal const string DownloadFolderPrefix = "CampertronDownload-";
        internal static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(60);

        internal static RidbRefreshResult Run(RidbRefreshRequest Request, IProgress<RidbRefreshProgress>? Progress, CancellationToken Token, RidbRefreshOptions? Options = null)
        {
            Options ??= new RidbRefreshOptions();
            if (string.IsNullOrWhiteSpace(Request.ConfigPath))
            {
                throw new ArgumentException("The settings folder is not set.", nameof(Request));
            }
            string ConfigPath = Request.ConfigPath;
            string CacheDirectory = Clear.CheckCacheDirectory(Request.CachePath, ConfigPath);
            Action<string> Log = Options.Log ?? (_ => { });
            long Started = Options.Time.GetTimestamp();
            var Reporter = new RidbRefreshReporter(Progress);
            Reporter.Stage(RidbRefreshStage.AcquiringLock);
            using (FileStream? RefreshLock = RidbStagedRefresh.TryAcquireRefreshLock(ConfigPath, out string LockProblem))
            {
                if (RefreshLock == null)
                {
                    return End(RidbRefreshOutcome.AnotherRefreshRunning, false, null, RidbRefreshResult.NoProblems, LockProblem, null);
                }
                string WorkRoot = Options.WorkRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string? DownloadFolder = null;
                string? OwnExtract = null;
                bool Installed = false;
                var Problems = new List<string>();
                try
                {
                    Reporter.Stage(RidbRefreshStage.CleaningUp);
                    RidbStagedRefresh.RemoveStaleStagingDirectories(ConfigPath, Log);
                    ConfigFileReplace.RemoveStaleTemporaryFiles(ConfigPath, ConfigFiles.GeneralFileName, Log);
                    RemoveStaleDownloadFolders(WorkRoot, Log);

                    // the first checkpoint: before anything is downloaded
                    Token.ThrowIfCancellationRequested();
                    string ExtractDirectory;
                    if (Options.ObtainExtractDirectory != null)
                    {
                        Reporter.Stage(RidbRefreshStage.Downloading);
                        ExtractDirectory = Options.ObtainExtractDirectory(Token);
                    }
                    else
                    {
                        Reporter.Stage(RidbRefreshStage.Preparing);
                        DownloadFolder = Path.Join(WorkRoot, DownloadFolderPrefix + Guid.NewGuid().ToString("N"));
                        if (Directory.Exists(DownloadFolder) || File.Exists(DownloadFolder))
                        {
                            throw new IOException("The download folder already exists.");
                        }
                        Directory.CreateDirectory(DownloadFolder);
                        string Archive = Path.Join(DownloadFolder, ArchiveName);
                        Reporter.Stage(RidbRefreshStage.Downloading);
                        Log($"Downloading {ArchiveName}");
                        Web.Download(Options.ExportUri, Archive, Options.Handler, Options.HeaderTimeout, Options.StallTimeout, Token,
                            (Done, Total) => Reporter.Count(Done, Total));
                        Token.ThrowIfCancellationRequested();
                        Reporter.Stage(RidbRefreshStage.Extracting);
                        Log($"Extracting {Archive}");
                        OwnExtract = Path.Join(WorkRoot, Compression.ExtractFolderName);
                        Compression.Extract(Archive, OwnExtract, Token, (Done, Count) => Reporter.Count(Done, Count));
                        ExtractDirectory = OwnExtract;
                        // the archive goes as soon as it is extracted, as before (about 570 MB less while the import runs)
                        RidbStagedRefresh.RemoveDirectory(DownloadFolder, Log);
                    }
                    RidbStagedRefresh.Outcome Built = RidbStagedRefresh.BuildValidateAndInstall(ExtractDirectory, ConfigPath, CacheDirectory, Problems, Log, Reporter, Token,
                        () => Installed = true);
                    if (Built == RidbStagedRefresh.Outcome.ValidationFailed)
                    {
                        return End(RidbRefreshOutcome.ValidationFailed, false, null, Problems.AsReadOnly(), null, null);
                    }
                    if (Built == RidbStagedRefresh.Outcome.InstalledButQuickCheckFailed)
                    {
                        return End(RidbRefreshOutcome.InstalledButCheckFailed, true, null, Problems.AsReadOnly(), null, null);
                    }
                }
                catch (Exception ex)
                {
                    RidbRefreshOutcome Outcome = Classify(Reporter.Current, ex, Token, Installed);
                    return End(Outcome, Installed, null, Problems.AsReadOnly(), null, ex);
                }
                finally
                {
                    // this refresh's own files, whatever happened: a successful import has deleted the extract folder already
                    // (the folder of a test's ObtainExtractDirectory is the caller's and is left to the import, as before)
                    if (DownloadFolder != null)
                    {
                        RidbStagedRefresh.RemoveDirectory(DownloadFolder, Log);
                    }
                    if (OwnExtract != null)
                    {
                        RidbStagedRefresh.RemoveDirectory(OwnExtract, Log);
                    }
                }

                // installed and checked: the refresh is recorded while the lock is still held, written once through a
                // validated temporary file (TD-37), only lastRidbDataRefresh (CM2). A failure is a warning: the data is new
                Reporter.Stage(RidbRefreshStage.RecordingRefresh);
                DateTime RefreshedAt = Options.UtcNow();
                try
                {
                    Yaml.GeneralConfigReplaceYaml(new GeneralConfig { LastRidbDataRefresh = RefreshedAt }, ConfigPath);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is YamlDotNet.Core.YamlException)
                {
                    return End(RidbRefreshOutcome.RefreshedWithoutTimestamp, true, RefreshedAt, RidbRefreshResult.NoProblems, ex.Message, ex);
                }
                catch (Exception ex)
                {
                    return End(RidbRefreshOutcome.Unexpected, true, RefreshedAt, RidbRefreshResult.NoProblems, null, ex);
                }
                Reporter.Stage(RidbRefreshStage.Completed);
                return End(RidbRefreshOutcome.Refreshed, true, RefreshedAt, RidbRefreshResult.NoProblems, null, null);
            }

            RidbRefreshResult End(RidbRefreshOutcome Outcome, bool Installed, DateTime? RefreshedAt, IReadOnlyList<string> Problems, string? Detail, Exception? Error)
            {
                return new RidbRefreshResult(Outcome, Reporter.Current, Installed, RefreshedAt, Options.Time.GetElapsedTime(Started), Problems, Detail, Error,
                    Token.IsCancellationRequested);
            }
        }

        // What an exception means, by the step it ended (business-rules.md §8): cancellation is only ever thrown at a
        // checkpoint before the cache step; a failure before the installation leaves the installed database unchanged.
        internal static RidbRefreshOutcome Classify(RidbRefreshStage Stage, Exception Error, CancellationToken Token, bool Installed)
        {
            if (Installed)
            {
                return RidbRefreshOutcome.Unexpected;
            }
            if (Error is OperationCanceledException && Token.IsCancellationRequested && Stage < RidbRefreshStage.ClearingCache)
            {
                return RidbRefreshOutcome.Canceled;
            }
            bool Storage = Error is IOException || Error is UnauthorizedAccessException;
            switch (Stage)
            {
                case RidbRefreshStage.Downloading:
                case RidbRefreshStage.Extracting:
                    if (Error is DownloadTimedOutException || (Error is OperationCanceledException && Error.InnerException is TimeoutException))
                    {
                        return RidbRefreshOutcome.DownloadTimedOut;
                    }
                    if (Error is HttpRequestException || Error.Data.Contains(Web.TransferFailureKey))
                    {
                        return RidbRefreshOutcome.DownloadFailed;
                    }
                    if (Error is InvalidDataException)
                    {
                        return RidbRefreshOutcome.ExtractionFailed;
                    }
                    return Storage ? RidbRefreshOutcome.StorageFailed : RidbRefreshOutcome.Unexpected;
                case RidbRefreshStage.PreparingDatabase:
                case RidbRefreshStage.Importing:
                case RidbRefreshStage.Saving:
                case RidbRefreshStage.Indexing:
                case RidbRefreshStage.Finalizing:
                    if (Error is SqliteException Sqlite)
                    {
                        return IsStorageError(Sqlite) ? RidbRefreshOutcome.StorageFailed : RidbRefreshOutcome.ImportFailed;
                    }
                    // a missing RIDB file or rows that are not the export's are the export's problem, not the disk's
                    if (Error is System.Text.Json.JsonException || Error is InvalidDataException || Error is FileNotFoundException || Error is NotSupportedException)
                    {
                        return RidbRefreshOutcome.ImportFailed;
                    }
                    return Storage ? RidbRefreshOutcome.StorageFailed : RidbRefreshOutcome.Unexpected;
                case RidbRefreshStage.ClearingCache:
                    // Clear.ClearRefreshCache refuses a folder that holds settings (InvalidOperationException)
                    return Storage || Error is InvalidOperationException ? RidbRefreshOutcome.StorageFailed : RidbRefreshOutcome.Unexpected;
                case RidbRefreshStage.Installing:
                    return Storage || Error is SqliteException ? RidbRefreshOutcome.InstallationFailed : RidbRefreshOutcome.Unexpected;
                case RidbRefreshStage.AcquiringLock:
                case RidbRefreshStage.CleaningUp:
                case RidbRefreshStage.Preparing:
                    return Storage ? RidbRefreshOutcome.StorageFailed : RidbRefreshOutcome.Unexpected;
                default:
                    return RidbRefreshOutcome.Unexpected;
            }
        }

        // SQLite's result codes for a file it can't read, write or open, a full disk, or a file that is not a database
        private static bool IsStorageError(SqliteException Error)
        {
            const int Perm = 3, ReadOnly = 8, IoErr = 10, Corrupt = 11, Full = 13, CantOpen = 14, NotADb = 26;
            int Primary = Error.SqliteErrorCode & 0xFF;
            return Primary == Perm || Primary == ReadOnly || Primary == IoErr || Primary == Corrupt || Primary == Full || Primary == CantOpen || Primary == NotADb;
        }

        // True only for this service's download folders: the prefix and a GUID in "N" format (32 lowercase hexadecimal
        // digits), as Guid.ToString("N") writes it.
        internal static bool IsDownloadFolderName(string Name)
        {
            if (!Name.StartsWith(DownloadFolderPrefix, StringComparison.Ordinal))
            {
                return false;
            }
            string Suffix = Name.Substring(DownloadFolderPrefix.Length);
            return Suffix.Length == 32 && Suffix.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')) && Guid.TryParseExact(Suffix, "N", out _);
        }

        // Download folders an interrupted refresh left in WorkRoot (a killed process: a canceled or failed refresh removes
        // its own). Called while holding the refresh lock, so no running refresh uses them; only exact names are touched,
        // and a link with such a name is removed as a link. A missing WorkRoot has none.
        private static void RemoveStaleDownloadFolders(string WorkRoot, Action<string> Log)
        {
            string Root = WorkRoot.Length == 0 ? "." : WorkRoot;
            if (!Directory.Exists(Root))
            {
                return;
            }
            foreach (string Candidate in Directory.GetDirectories(Root))
            {
                string Name = Path.GetFileName(Candidate);
                if (IsDownloadFolderName(Name) && RidbStagedRefresh.RemoveDirectory(Path.Join(WorkRoot, Name), Log))
                {
                    Log($"Removed {Path.Join(WorkRoot, Name)}, left by an interrupted RIDB refresh");
                }
            }
        }
    }

    // Reports a refresh's steps through its progress (any thread; the caller's IProgress decides where they go) and keeps
    // the step it is in, which tells a failure's meaning (RidbRefreshService.Classify). From ClearingCache on it reports
    // CanCancel false.
    internal sealed class RidbRefreshReporter
    {
        private readonly IProgress<RidbRefreshProgress>? _Progress;
        private long _Sequence;

        internal RidbRefreshReporter(IProgress<RidbRefreshProgress>? Progress)
        {
            _Progress = Progress;
        }

        internal RidbRefreshStage Current { get; private set; } = RidbRefreshStage.AcquiringLock;
        internal bool CanCancel => Current < RidbRefreshStage.ClearingCache;

        internal void Stage(RidbRefreshStage Stage)
        {
            Current = Stage;
            Report(null, null);
        }

        internal void Count(long Done, long? Total)
        {
            Report(Done, Total);
        }

        private void Report(long? Done, long? Total)
        {
            _Progress?.Report(new RidbRefreshProgress(Interlocked.Increment(ref _Sequence), Current, CanCancel, Done, Total));
        }
    }
}
