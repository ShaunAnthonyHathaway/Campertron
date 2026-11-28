using System.Security.Cryptography;
using CampertronLibrary.function.Base;
using YamlDotNet.Core;

namespace CampertronLibrary.function.Transfer.Apply
{
    // What the host of an import knows of its own process, asked after the confirmation and before the lock (D132, D134;
    // v0.4.0-settings-transfer.md §Apply lock): an unsaved change in an editor of a settings or search file (the web UI: in the
    // importing session's forms), a refresh of the campground data that runs, a monitoring that is starting. Each host answers
    // for itself; a running, stopped or held monitoring and a manual search are no reason (they write no settings file).
    internal sealed class TransferApplyHost
    {
        internal TransferApplyHost(Func<bool> HasUnsavedChanges, Func<bool> IsRefreshing, Func<bool> IsMonitoringStarting)
        {
            this.HasUnsavedChanges = HasUnsavedChanges;
            this.IsRefreshing = IsRefreshing;
            this.IsMonitoringStarting = IsMonitoringStarting;
        }
        internal Func<bool> HasUnsavedChanges { get; }
        internal Func<bool> IsRefreshing { get; }
        internal Func<bool> IsMonitoringStarting { get; }
    }

    internal enum TransferApplyOutcome
    {
        // every file written, read back and checked; committed
        Applied,
        // refused before anything was written
        Refused,
        // failed after the lock; the rollback put every file back (or nothing had been written)
        FailedNothingChanged,
        // failed, and the rollback could not put everything back: the backup set is kept, the names are said
        FailedNotRestored
    }

    // An apply's result: its outcome, its one fixed sentence, the warnings of a successful apply (an earlier secret file left),
    // and after a failure the logical names restored and not restored (files' names, a secret file by its kind: never a path or
    // a value)
    internal sealed class TransferApplyResult
    {
        internal TransferApplyResult(TransferApplyOutcome Outcome, string Message, TransferImportFailure? Failure, IReadOnlyList<string> Warnings,
            IReadOnlyList<string> Restored, IReadOnlyList<string> NotRestored)
        {
            this.Outcome = Outcome;
            this.Message = Message;
            this.Failure = Failure;
            this.Warnings = Warnings;
            this.Restored = Restored;
            this.NotRestored = NotRestored;
        }
        internal TransferApplyOutcome Outcome { get; }
        internal string Message { get; }
        internal TransferImportFailure? Failure { get; }
        internal IReadOnlyList<string> Warnings { get; }
        internal IReadOnlyList<string> Restored { get; }
        internal IReadOnlyList<string> NotRestored { get; }
        internal bool Applied => Outcome == TransferApplyOutcome.Applied;
        internal IReadOnlyList<string> RestartWords => Applied ? TransferImportWording.RestartWords : Array.Empty<string>();
        public override string ToString() => Outcome + ": " + Message;
    }

    // The import's apply (D132, D134; v0.4.0-settings-transfer.md §Confirmation, §Apply lock, §Backup set, §Write order, §Atomic
    // replacement, §Read-back verification, §Rollback, §Rollback failure, §Cancellation boundary). After the caller's explicit
    // confirmation of the preview's revision:
    //  1. the preview is taken once (a second apply of it is refused), and nothing is written when it can't be applied, the
    //     confirmation is not of it, the caller cancelled, an editor has unsaved changes, a refresh runs or the monitoring starts;
    //  2. the apply lock (TransferApplyLock) is taken and held to the end; a held lock refuses;
    //  3. under it the candidate is built again from the folder as it is now and must give the same revision (the stale check);
    //     from here on nothing is cancelled: the apply ends applied or rolled back;
    //  4. the owner-only, secret-free backup set (TransferBackupSet) takes the previous set's place;
    //  5. each imported secret's new file is created owner-only (TransferProtection) and read back before anything names it;
    //  6. the search files, then Ntfy.yaml, then Email.yaml, and General.yaml last, each through the safe writers
    //     (ConfigFileReplace.Replace, or Create for a new file) and read back; a file whose text is unchanged is not written;
    //  7. every written file is read again through the bounded loader and the validation, with the values it must hold, each
    //     secret file through SecretFile and its protection, the receiver's search files still there, the web UI's files untouched;
    //  8. committed; then each earlier secret file of Campertron's the import superseded is removed only when nothing can still
    //     need it (else it stays, with a fixed warning); the lock is released.
    // Any failure in 4 to 8 before the commit rolls back, in the same process and under the same lock (Rollback). It starts,
    // stops or restarts no monitoring, runs no search, refreshes nothing, touches no Camp.History, campground data or cache,
    // and keeps no journal: no start ever reads, replays or undoes an import.
    internal static class TransferApply
    {
        // the apply's steps, for the tests' failure injection; does nothing in production
        internal static Action<string, string?> Step = (Name, Path_) => { };

        internal static TransferApplyResult Apply(TransferPreview Preview, string? ConfirmedRevision, TransferApplyHost Host, CancellationToken Cancel = default)
        {
            ArgumentNullException.ThrowIfNull(Preview);
            ArgumentNullException.ThrowIfNull(Host);
            if (!Preview.CanApply)
            {
                return Refused(TransferImportProblem.NotApplicable);
            }
            if (!string.Equals(ConfirmedRevision, Preview.Revision, StringComparison.Ordinal))
            {
                return Refused(TransferImportProblem.NotConfirmed);
            }
            if (Interlocked.Exchange(ref Preview.Used, 1) != 0)
            {
                return Refused(TransferImportProblem.AlreadyUsed);
            }
            if (Cancel.IsCancellationRequested)
            {
                return Refused(TransferImportProblem.Cancelled);
            }
            // §Apply lock's order: unsaved changes, a refresh, a starting monitoring, the lock, the stale check
            if (Host.HasUnsavedChanges())
            {
                return Refused(TransferImportProblem.UnsavedChanges);
            }
            if (Host.IsRefreshing())
            {
                return Refused(TransferImportProblem.RefreshRunning);
            }
            if (Host.IsMonitoringStarting())
            {
                return Refused(TransferImportProblem.MonitoringStarting);
            }
            if (Cancel.IsCancellationRequested)
            {
                return Refused(TransferImportProblem.Cancelled);
            }
            TransferPlan Previewed = Preview.Plan;
            FileStream? Lock;
            TransferLockProblem? LockProblem;
            try
            {
                Step("lock", null);
                Lock = TransferApplyLock.TryAcquire(Previewed.ConfigPath, out LockProblem);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return Refused(TransferImportProblem.LockUnavailable);
            }
            if (Lock == null)
            {
                return Refused(LockProblem == TransferLockProblem.Held ? TransferImportProblem.LockHeld : TransferImportProblem.LockUnavailable);
            }
            // the write boundary: from here on the cancellation is not looked at again, and every end releases the lock
            try
            {
                TransferPreview Fresh;
                try
                {
                    Step("stale-check", null);
                    Fresh = TransferImport.Build(Previewed.ConfigPath, Previewed.Package, Previewed.Choices, Previewed.TransactionId, Previewed.Place, DateTime.Now);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    return Refused(TransferImportProblem.Stale);
                }
                if (!string.Equals(Fresh.Revision, Preview.Revision, StringComparison.Ordinal) || !Fresh.CanApply)
                {
                    return Refused(TransferImportProblem.Stale);
                }
                var Run = new ApplyRun(Fresh.Plan);
                return Run.Execute();
            }
            finally
            {
                try
                {
                    Step("release", null);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // the lock is released below in any case
                }
                Lock.Dispose();
            }
        }

        private static TransferApplyResult Refused(TransferImportProblem Problem)
        {
            TransferImportFailure Failure = TransferImportWording.Apply(Problem);
            return new TransferApplyResult(TransferApplyOutcome.Refused, Failure.Message, Failure, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        }

        // one apply's run under its lock: what it wrote, so that a failure can put it back
        private sealed class ApplyRun
        {
            private readonly TransferPlan Plan;
            // the writes and secret files this run began (a failure in the middle of one leaves it old or new: both are checked)
            private readonly List<TransferWrite> Attempted = new();
            private readonly List<TransferSecretWrite> SecretsAttempted = new();

            internal ApplyRun(TransferPlan Plan)
            {
                this.Plan = Plan;
            }

            internal TransferApplyResult Execute()
            {
                try
                {
                    TransferBackupSet.Install(Plan.ConfigPath, Plan.BackupFiles, Plan.BackupContents, Step);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // nothing of the settings was written, and the old set is as it was
                    return Failed(TransferImportProblem.FailedNothingChanged);
                }
                try
                {
                    foreach (TransferSecretWrite Secret in Plan.Secrets)
                    {
                        CreateSecret(Secret);
                    }
                    foreach (TransferWrite Write in Plan.Writes)
                    {
                        WriteFile(Write);
                    }
                    Verify();
                    Step("commit", null);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    return Rollback();
                }
                // committed: nothing is rolled back from here on
                List<string> Warnings = CleanUpSuperseded();
                return new TransferApplyResult(TransferApplyOutcome.Applied, TransferImportWording.Applied, null, Warnings, Array.Empty<string>(), Array.Empty<string>());
            }

            private static TransferApplyResult Failed(TransferImportProblem Problem)
            {
                TransferImportFailure Failure = TransferImportWording.Apply(Problem);
                return new TransferApplyResult(TransferApplyOutcome.FailedNothingChanged, Failure.Message, Failure, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            }

            // ---- the writes ----

            // An imported secret's new file: a temporary file created owner-only with the secret, verified, then moved into place
            // only if the name is free (create-only: never Email.password, Ntfy.token, an earlier transaction's file or a user's
            // file), and verified again: owner-only, the secret's bytes
            private void CreateSecret(TransferSecretWrite Secret)
            {
                string Kind = Secret.Kind == TransferSecretKind.SmtpPassword ? "smtp-password" : "ntfy-token";
                Step("secret-create:" + Kind, Secret.FullPath);
                if (TransferProtection.Exists(Secret.FullPath))
                {
                    throw new IOException("The new secret file's name is taken.");
                }
                SecretsAttempted.Add(Secret);
                string Temporary = Path.Join(Plan.ConfigPath, ConfigFileReplace.TemporaryName(Secret.FileName));
                try
                {
                    TransferProtection.CreateOwnerOnlyFile(Temporary, Secret.Bytes);
                    Step("secret-permission:" + Kind, Temporary);
                    Step("secret-verify:" + Kind, Temporary);
                    if (!TransferProtection.IsOwnerOnlyFile(Temporary) || !SameBytes(Temporary, Secret.Bytes))
                    {
                        throw new IOException("The new secret file is not owner-only or not as written.");
                    }
                    File.Move(Temporary, Secret.FullPath, overwrite: false);
                    Step("secret-placed:" + Kind, Secret.FullPath);
                    if (!TransferProtection.IsOwnerOnlyFile(Secret.FullPath) || !SameBytes(Secret.FullPath, Secret.Bytes))
                    {
                        throw new IOException("The new secret file did not read back owner-only and as written.");
                    }
                }
                finally
                {
                    RemoveOwnTemporary(Temporary, Secret.Bytes);
                }
            }

            private void WriteFile(TransferWrite Write)
            {
                Step("write:" + Write.Name, Write.FullPath);
                Attempted.Add(Write);
                string? Verify(string Written) => string.Equals(Written, Write.Text, StringComparison.Ordinal) ? null : "it is not the import's text";
                if (Write.Create)
                {
                    if (!ConfigFileReplace.Create(Write.FullPath, Write.Text, Verify, Write.CreateMode))
                    {
                        throw new IOException("A file of the import's name was created meanwhile.");
                    }
                }
                else
                {
                    ConfigFileReplace.Replace(Write.Original!, Write.Text, Verify, Write.ForwardBackup);
                }
                // NEGATIVE CONTROL W2 (never for merge): the settings file given a protected owner-only list of its own
                if (OperatingSystem.IsWindows())
                {
                    var Security = new System.Security.AccessControl.FileSecurity();
                    Security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                    using var Identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                    Security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(Identity.User!, System.Security.AccessControl.FileSystemRights.FullControl,
                        System.Security.AccessControl.AccessControlType.Allow));
                    System.IO.FileSystemAclExtensions.SetAccessControl(new FileInfo(Write.FullPath), Security);
                }
                Step("readback:" + Write.Name, Write.FullPath);
                if (!ConfigFileReplace.Read(Write.FullPath).Bytes.AsSpan().SequenceEqual(Write.Bytes))
                {
                    throw new IOException("A written file did not read back as written.");
                }
            }

            // ---- the read-back ----

            private void Verify()
            {
                Step("verify", null);
                foreach (TransferWrite Write in Plan.Writes)
                {
                    ConfigFileReplace.SettingsFile Now = ConfigFileReplace.Read(Write.FullPath);
                    if (!Now.Bytes.AsSpan().SequenceEqual(Write.Bytes))
                    {
                        throw new IOException("A written file changed.");
                    }
                    object Settings = LoadAs(Write.Kind, Now.Text) ?? throw new InvalidDataException("A written file does not load.");
                    if (Blocking(Write, Now.Text))
                    {
                        throw new InvalidDataException("A written file does not pass its validation.");
                    }
                    IReadOnlyDictionary<string, System.Reflection.PropertyInfo> Keys = YamlDocument.ModelKeys(Settings.GetType());
                    foreach ((string Key, object? Value) in Write.Expected)
                    {
                        if (!ConfigEditSession<GeneralConfig>.SameValue(Keys[Key].PropertyType, Keys[Key].GetValue(Settings), Value))
                        {
                            throw new InvalidDataException("A written file does not hold the values it must.");
                        }
                    }
                }
                Step("compare", null);
                // the receiver's search files are all still there, and every file the import adds
                List<string> Searches = ConfigFiles.SortedSearchFileNames(Plan.ConfigPath);
                if (Plan.ReceiverSearches.Concat(Plan.Writes.Where(W => W.Kind == ConfigFileKind.Search).Select(W => W.Name)).Any(N => !Searches.Contains(N, StringComparer.Ordinal)))
                {
                    throw new IOException("A search file is missing.");
                }
                foreach ((string Name, string Facts) in Plan.Untouched)
                {
                    if (TransferProtection.MetadataFacts(Path.Join(Plan.ConfigPath, Name)) != Facts)
                    {
                        throw new IOException("A file the import never writes changed.");
                    }
                }
                Step("secret-reference", null);
                foreach (TransferSecretWrite Secret in Plan.Secrets)
                {
                    TransferWrite Names = Plan.Writes.Single(W => W.Kind == (Secret.Kind == TransferSecretKind.SmtpPassword ? ConfigFileKind.Email : ConfigFileKind.Ntfy));
                    string? Reference = LoadAs(Names.Kind, ConfigFileReplace.Read(Names.FullPath).Text) switch
                    {
                        EmailConfig E => E.SmtpPasswordFile,
                        NtfyConfig N => N.TokenFile,
                        _ => null
                    };
                    if (!string.Equals(Reference, Secret.FullPath, StringComparison.Ordinal) || !TransferProtection.IsOwnerOnlyFile(Secret.FullPath))
                    {
                        throw new IOException("A new secret file is not named or not owner-only.");
                    }
                    // the one reader of a secret file, as the monitoring's start reads it: the value is compared in memory only
                    if (SecretFile.Read(Secret.FullPath, out string? Value) != null || Value == null
                        || !CryptographicOperations.FixedTimeEquals(TransferFormat.StrictUtf8.GetBytes(Value), Secret.Bytes))
                    {
                        throw new IOException("A new secret file does not hold its secret.");
                    }
                }
            }

            private static object? LoadAs(ConfigFileKind Kind, string Text)
            {
                try
                {
                    return Kind switch
                    {
                        ConfigFileKind.General => Yaml.LoadSettingsText<GeneralConfig>(Text),
                        ConfigFileKind.Email => Yaml.LoadSettingsText<EmailConfig>(Text),
                        ConfigFileKind.Ntfy => Yaml.LoadSettingsText<NtfyConfig>(Text),
                        _ => Yaml.LoadSettingsText<CampertronConfig>(Text)
                    };
                }
                catch (Exception ex) when (ex is YamlException || ex is Yaml.SettingsLimitException)
                {
                    return null;
                }
            }

            // what the build refused in a file's validation (an error, but ntfy's missing topic, which stays incomplete)
            private bool Blocking(TransferWrite Write, string Text)
            {
                List<ConfigDiagnostic> Found = Write.Kind switch
                {
                    ConfigFileKind.General => ConfigValidation.ValidateGeneral(Text, DateTime.Now, Write.Name),
                    ConfigFileKind.Email => ConfigValidation.ValidateEmail(Text, Plan.ResultOutput == OutputType.Email, Write.Name),
                    ConfigFileKind.Ntfy => ConfigValidation.ValidateNtfy(Text, Plan.ResultOutput == OutputType.Ntfy, Write.Name),
                    _ => ConfigValidation.ValidateSearch(Text, Write.Name, DateTime.Now, Plan.ResultOutput)
                };
                return Found.Any(D => D.Severity == ConfigSeverity.Error && !(Write.Kind == ConfigFileKind.Ntfy && D.Code == ConfigCode.NtfyTopicMissing));
            }

            // ---- the rollback ----

            // In the reverse of the write order: each replaced file put back to its exact previous bytes through the safe writer
            // (its rolling backup secret-free), only while it is still what this apply wrote; each created file removed only
            // while it is still exactly what this apply wrote; each new secret file removed only while it is still this
            // apply's (owner-only, its bytes) and no settings file names it. Anything else is left and named. Then every file is
            // read again: the result says nothing changed only when all is as before
            private TransferApplyResult Rollback()
            {
                var Restored = new List<string>();
                var NotRestored = new List<string>();
                foreach (TransferWrite Write in Enumerable.Reverse(Attempted))
                {
                    bool Back;
                    try
                    {
                        Step("rollback:" + Write.Name, Write.FullPath);
                        Back = Restore(Write);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        Back = false;
                    }
                    (Back ? Restored : NotRestored).Add(Write.Name);
                }
                foreach (TransferSecretWrite Secret in Enumerable.Reverse(SecretsAttempted))
                {
                    bool Removed;
                    try
                    {
                        Step("rollback:" + Secret.FileName, Secret.FullPath);
                        Removed = RemoveSecret(Secret);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        Removed = false;
                    }
                    if (!Removed)
                    {
                        NotRestored.Add(TransferImportWording.SecretFileWords(Secret.Kind));
                    }
                }
                try
                {
                    Step("rollback-verify", null);
                    foreach (TransferWrite Write in Attempted.Where(W => !NotRestored.Contains(W.Name)))
                    {
                        bool AsBefore = Write.Create ? !TransferProtection.Exists(Write.FullPath) : ConfigFileReplace.Read(Write.FullPath).Bytes.AsSpan().SequenceEqual(Write.Original!.Bytes);
                        if (!AsBefore)
                        {
                            Restored.Remove(Write.Name);
                            NotRestored.Add(Write.Name);
                        }
                    }
                    foreach (TransferSecretWrite Secret in SecretsAttempted.Where(S => TransferProtection.Exists(S.FullPath)))
                    {
                        string Words = TransferImportWording.SecretFileWords(Secret.Kind);
                        if (!NotRestored.Contains(Words))
                        {
                            NotRestored.Add(Words);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    foreach (string Name in Restored)
                    {
                        NotRestored.Add(Name);
                    }
                    Restored.Clear();
                }
                if (NotRestored.Count == 0)
                {
                    return Failed(TransferImportProblem.FailedNothingChanged);
                }
                TransferImportFailure Failure = TransferImportWording.NotRestored(Restored, NotRestored);
                return new TransferApplyResult(TransferApplyOutcome.FailedNotRestored, Failure.Message, Failure, Array.Empty<string>(), Restored, NotRestored);
            }

            // a write undone: true when the file is as it was before the apply
            private static bool Restore(TransferWrite Write)
            {
                if (Write.Create)
                {
                    if (!TransferProtection.Exists(Write.FullPath))
                    {
                        return true;
                    }
                    // ER-14's one exception (D134): a file this apply created moments before, still exactly what it wrote
                    var Info = new FileInfo(Write.FullPath);
                    if (!Info.Exists || Info.LinkTarget != null || (Info.Attributes & FileAttributes.ReparsePoint) != 0 || !SameBytes(Write.FullPath, Write.Bytes))
                    {
                        return false;
                    }
                    File.Delete(Write.FullPath);
                    return !TransferProtection.Exists(Write.FullPath);
                }
                ConfigFileReplace.SettingsFile Now = ConfigFileReplace.Read(Write.FullPath);
                if (Now.Bytes.AsSpan().SequenceEqual(Write.Original!.Bytes))
                {
                    return true;
                }
                if (!Now.Bytes.AsSpan().SequenceEqual(Write.Bytes))
                {
                    // changed by another program since this apply wrote it: never written over
                    return false;
                }
                string Previous = Write.Original.Text;
                ConfigFileReplace.Replace(Now, Previous, Written => string.Equals(Written, Previous, StringComparison.Ordinal) ? null : "it is not the earlier text", Write.RollbackBackup);
                return ConfigFileReplace.Read(Write.FullPath).Bytes.AsSpan().SequenceEqual(Write.Original.Bytes);
            }

            // a new secret file removed: only while it is this apply's (owner-only, its bytes, no link) and no settings file names it
            private bool RemoveSecret(TransferSecretWrite Secret)
            {
                if (!TransferProtection.Exists(Secret.FullPath))
                {
                    return true;
                }
                if (!TransferProtection.IsOwnerOnlyFile(Secret.FullPath) || !SameBytes(Secret.FullPath, Secret.Bytes) || IsNamed(Secret.FullPath))
                {
                    return false;
                }
                File.Delete(Secret.FullPath);
                return !TransferProtection.Exists(Secret.FullPath);
            }

            // whether Email.yaml's smtpPasswordFile or Ntfy.yaml's tokenFile names the file now; true when it can't be told
            private bool IsNamed(string FullPath)
            {
                StringComparison Names = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                foreach (string File_ in new[] { ConfigFiles.EmailFileName, ConfigFiles.NtfyFileName })
                {
                    string Settings = Path.Join(Plan.ConfigPath, File_);
                    if (!TransferProtection.Exists(Settings))
                    {
                        continue;
                    }
                    object? Model;
                    try
                    {
                        Model = LoadAs(File_ == ConfigFiles.EmailFileName ? ConfigFileKind.Email : ConfigFileKind.Ntfy, ConfigFileReplace.Read(Settings).Text);
                    }
                    catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
                    {
                        return true;
                    }
                    string? Reference = Model switch
                    {
                        EmailConfig E => E.SmtpPasswordFile,
                        NtfyConfig N => N.TokenFile,
                        _ => null
                    };
                    if (Model == null || (Reference != null && Path.IsPathFullyQualified(Reference) && string.Equals(Path.GetFullPath(Reference), FullPath, Names)))
                    {
                        return true;
                    }
                }
                return false;
            }

            // ---- after the commit ----

            // Each earlier secret file of Campertron's the import superseded (its name Email.password.<id> or Ntfy.token.<id> in
            // this settings folder, named before the apply): removed only when its metadata is still what the build read under
            // the lock, it is owner-only, it is not this apply's, and no settings file names it now. Anything uncertain stays,
            // with one fixed warning that holds no path; the import stays applied. Never a secure deletion
            private List<string> CleanUpSuperseded()
            {
                var Warnings = new List<string>();
                foreach (TransferOldSecret Old in Plan.OldSecrets)
                {
                    try
                    {
                        Step("cleanup:" + (Old.Kind == TransferSecretKind.SmtpPassword ? "smtp-password" : "ntfy-token"), Old.FullPath);
                        if (!TransferProtection.Exists(Old.FullPath))
                        {
                            continue;
                        }
                        bool Proven = TransferProtection.MetadataFacts(Old.FullPath) == Old.Facts && TransferProtection.IsOwnerOnlyFile(Old.FullPath)
                            && !Plan.Secrets.Any(S => string.Equals(S.FullPath, Old.FullPath, StringComparison.Ordinal)) && !IsNamed(Old.FullPath);
                        if (!Proven)
                        {
                            Warnings.Add(TransferImportWording.CleanupWarning);
                            continue;
                        }
                        File.Delete(Old.FullPath);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        Warnings.Add(TransferImportWording.CleanupWarning);
                    }
                }
                return Warnings.Distinct().ToList();
            }

            // ---- helpers ----

            private static bool SameBytes(string FullPath, byte[] Expected)
            {
                var Info = new FileInfo(FullPath);
                if (!Info.Exists || Info.Length != Expected.Length)
                {
                    return false;
                }
                byte[] Read = File.ReadAllBytes(FullPath);
                try
                {
                    return CryptographicOperations.FixedTimeEquals(Read, Expected);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(Read);
                }
            }

            // the secret writer's temporary file, removed when it is still the one it wrote (a regular file, no link)
            private static void RemoveOwnTemporary(string Temporary, byte[] Bytes)
            {
                try
                {
                    var Info = new FileInfo(Temporary);
                    if (Info.Exists && Info.LinkTarget == null && SameBytes(Temporary, Bytes))
                    {
                        File.Delete(Temporary);
                    }
                }
                catch (Exception ex) when (TransferProtection.IsFileSystemFailure(ex))
                {
                    // an owner-only file of a temporary name: left, and no loader reads it
                }
            }
        }
    }
}
