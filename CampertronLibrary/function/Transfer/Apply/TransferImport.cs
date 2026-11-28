using System.Security.Cryptography;
using System.Text;
using CampertronLibrary.function.Base;
using YamlDotNet.Core;

namespace CampertronLibrary.function.Transfer.Apply
{
    // An import's choices (D134): every entry and every carried secret by default, as D132 imports a carried secret unless a rule
    // refuses it; a caller may leave out entries (by name) and secrets. A secret goes only with its settings file.
    internal sealed class TransferImportChoices
    {
        internal static readonly TransferImportChoices Everything = new TransferImportChoices();
        internal IReadOnlyCollection<string> LeftOut { get; init; } = Array.Empty<string>();
        internal IReadOnlyCollection<TransferSecretKind> SecretsLeftOut { get; init; } = Array.Empty<TransferSecretKind>();
    }

    // what the import does with one file of the package
    internal enum TransferImportFileChange
    {
        Created,
        Replaced,
        Unchanged,
        LeftOut
    }

    // where a secret comes from on this computer before the import, from the settings files only: an environment variable is
    // never read on the receiver (v0.4.0-settings-transfer.md §Environment-variable handling)
    internal enum TransferImportSecretSource
    {
        File,
        VariableOrPlainText,
        VariableOrNone,
        Topic
    }

    internal enum TransferImportSecretDecision
    {
        Imported,
        LeftOut,
        FileLeftOut,
        RefusedReadableByOthers,
        RefusedUnusable
    }

    internal enum TransferOutputToDecision
    {
        // the package carries no General.yaml, or it is left out
        NotCarried,
        // the package's outputTo is this computer's
        Unchanged,
        // a change the readiness checks passed (or one to Console or HtmlFile, which no start monitors with)
        Accepted,
        // a change to Email or Ntfy the readiness checks refused: left out, this computer's outputTo kept
        Excluded
    }

    // one file of the preview: its name, what the import does with it, and for a settings file the lines that change, with
    // every secret's and kept key's value masked (never a value of a secret, a kept key or a path)
    internal sealed class TransferPreviewFile
    {
        internal TransferPreviewFile(string Name, bool IsSearch, TransferImportFileChange Change, IReadOnlyList<ConfigPreviewLine> Lines)
        {
            this.Name = Name;
            this.IsSearch = IsSearch;
            this.Change = Change;
            this.Lines = Lines;
        }
        internal string Name { get; }
        internal bool IsSearch { get; }
        internal TransferImportFileChange Change { get; }
        internal IReadOnlyList<ConfigPreviewLine> Lines { get; }
        internal string Words => Change switch
        {
            TransferImportFileChange.Created => TransferImportWording.Created(Name),
            TransferImportFileChange.Replaced => TransferImportWording.Replaced(Name),
            TransferImportFileChange.Unchanged => TransferImportWording.Unchanged(Name),
            _ => TransferImportWording.LeftOut(Name)
        };
        public override string ToString() => Name + " " + Change;
    }

    // one secret the package carries: its kind, what the import does with it, and the change of its source kind (never a value)
    internal sealed class TransferPreviewSecret
    {
        internal TransferPreviewSecret(TransferSecretKind Kind, TransferImportSecretDecision Decision, TransferImportSecretSource Before)
        {
            this.Kind = Kind;
            this.Decision = Decision;
            this.Before = Before;
        }
        internal TransferSecretKind Kind { get; }
        internal TransferImportSecretDecision Decision { get; }
        internal TransferImportSecretSource Before { get; }
        internal IReadOnlyList<string> Words => Decision == TransferImportSecretDecision.Imported
            ? (Kind == TransferSecretKind.NtfyTopic
                ? new[] { TransferImportWording.SecretImported(Kind) }
                : new[] { TransferImportWording.SecretImported(Kind), TransferImportWording.SourceChange(Kind, TransferImportWording.SourceBefore(Before, Kind)) })
            : new[] { TransferImportWording.SecretNotImported(Kind, Decision) };
        public override string ToString() => Kind + " " + Decision;
    }

    // the resulting outputTo and what it means (§outputTo gate)
    internal sealed class TransferPreviewOutputTo
    {
        internal TransferPreviewOutputTo(OutputType? Receiver, OutputType? Package, OutputType? Result, TransferOutputToDecision Decision, IReadOnlyList<ConfigDiagnostic> Findings,
            IReadOnlyList<ConfigDiagnostic> ResultFindings)
        {
            this.Receiver = Receiver;
            this.Package = Package;
            this.Result = Result;
            this.Decision = Decision;
            this.Findings = Findings;
            this.ResultFindings = ResultFindings;
        }
        internal OutputType? Receiver { get; }
        internal OutputType? Package { get; }
        internal OutputType? Result { get; }
        internal TransferOutputToDecision Decision { get; }
        // the readiness checks' own findings that excluded the change
        internal IReadOnlyList<ConfigDiagnostic> Findings { get; }
        // what the same checks find for the resulting outputTo when it is this computer's and is Email or Ntfy: a warning, as
        // the editors warn about a saved outputTo (D73); never a reason the import is refused
        internal IReadOnlyList<ConfigDiagnostic> ResultFindings { get; }
        internal IReadOnlyList<string> Words
        {
            get
            {
                var Out = new List<string>();
                if (Decision == TransferOutputToDecision.Excluded && Package is OutputType P && Result is OutputType Kept)
                {
                    Out.Add(TransferImportWording.OutputToExcluded(P, Kept, Findings));
                }
                if (Result is OutputType R)
                {
                    Out.Add(TransferImportWording.OutputToResult(R));
                    if (ResultFindings.Count > 0)
                    {
                        Out.Add(TransferImportWording.OutputToWarning(R, ResultFindings));
                    }
                }
                return Out;
            }
        }
        public override string ToString() => $"outputTo {Decision}: {Result}";
    }

    // The preview of an import (D132, D134; v0.4.0-settings-transfer.md §Preview), built by the transfer core from the candidate;
    // an adapter only shows it. It holds no secret value, passphrase, environment value or path, and its words are fixed
    // sentences. Its revision binds the package, the receiver's state it was built from (each file's identity, content and
    // permissions), the choices, the decisions and the candidate's bytes; it is applied at most once (TransferApply).
    internal sealed class TransferPreview
    {
        internal TransferPreview(string Revision, IReadOnlyList<TransferImportFailure> Refusals, string PackageWords, IReadOnlyList<TransferPreviewFile> Files,
            IReadOnlyList<string> Preserved, IReadOnlyList<(string File, string Key)> Kept, IReadOnlyList<TransferPreviewSecret> Secrets, TransferPreviewOutputTo OutputTo,
            IReadOnlyList<ConfigDiagnostic> Findings, IReadOnlyList<string> Warnings, bool NtfyIncomplete, TransferPlan Plan)
        {
            this.Revision = Revision;
            this.Refusals = Refusals;
            this.PackageWords = PackageWords;
            this.Files = Files;
            this.Preserved = Preserved;
            this.Kept = Kept;
            this.Secrets = Secrets;
            this.OutputTo = OutputTo;
            this.Findings = Findings;
            this.Warnings = Warnings;
            this.NtfyIncomplete = NtfyIncomplete;
            this.Plan = Plan;
        }
        // 64 lower-case hexadecimal digits: a keyed digest (HMAC-SHA256 with a key of this process), so it says nothing about
        // what it binds
        internal string Revision { get; }
        internal bool CanApply => Refusals.Count == 0;
        // why it can't be applied (each a fixed sentence)
        internal IReadOnlyList<TransferImportFailure> Refusals { get; }
        internal string PackageWords { get; }
        internal IReadOnlyList<TransferPreviewFile> Files { get; }
        // this computer's search files the package does not touch: not changed, never deleted
        internal IReadOnlyList<string> Preserved { get; }
        // the receiver-kept values: kept from this computer, never shown
        internal IReadOnlyList<(string File, string Key)> Kept { get; }
        internal IReadOnlyList<TransferPreviewSecret> Secrets { get; }
        internal TransferPreviewOutputTo OutputTo { get; }
        // the validation's warnings and facts of the files the import writes, in its own value-free words
        internal IReadOnlyList<ConfigDiagnostic> Findings { get; }
        // the preview's own warnings: what stays incomplete, a secret file's path being this computer's
        internal IReadOnlyList<string> Warnings { get; }
        internal bool NtfyIncomplete { get; }
        internal IReadOnlyList<string> RestartWords => TransferImportWording.RestartWords;
        internal string BackupWords => TransferImportWording.BackupWords;
        // the plan the apply runs: never shown
        internal TransferPlan Plan { get; }
        // 0 until an apply took this preview (Interlocked): a preview is applied at most once
        internal int Used;

        // every sentence of the preview, in its order, for an adapter that shows text
        internal IReadOnlyList<string> Lines()
        {
            var Out = new List<string> { PackageWords };
            Out.AddRange(Files.Select(F => F.Words));
            if (Preserved.Count > 0 || Files.Any(F => F.IsSearch))
            {
                Out.Add(TransferImportWording.OthersNotTouched);
            }
            Out.AddRange(Kept.Select(K => TransferImportWording.Kept(K.File, K.Key)));
            Out.AddRange(Secrets.SelectMany(S => S.Words));
            Out.AddRange(OutputTo.Words);
            Out.AddRange(Warnings);
            Out.AddRange(Findings.Select(TransferImportWording.Validation));
            Out.AddRange(Refusals.Select(R => R.Message));
            Out.Add(BackupWords);
            Out.AddRange(RestartWords);
            return Out;
        }

        public override string ToString() => $"TransferPreview {Revision.Substring(0, 8)}: {Files.Count} files" + (CanApply ? string.Empty : ", can't be applied");
    }

    // One write of the plan: a settings or search file created or replaced. Its texts can hold the receiver's own plain-text
    // password and an imported topic, so ToString holds the name only
    internal sealed class TransferWrite
    {
        internal string Name { get; init; } = string.Empty;
        internal string FullPath { get; init; } = string.Empty;
        internal ConfigFileKind Kind { get; init; }
        internal bool Create { get; init; }
        internal string Text { get; init; } = string.Empty;
        internal byte[] Bytes { get; init; } = Array.Empty<byte>();
        // a replacement: the file as read under the lock, its text and bytes for the rollback
        internal ConfigFileReplace.SettingsFile? Original { get; init; }
        internal UnixFileMode? CreateMode { get; init; }
        // Email.yaml and Ntfy.yaml: the rolling backup's text without the password and topic, forward (the original's) and
        // back (the candidate's)
        internal ConfigFileReplace.SecretFreeBackup? ForwardBackup { get; init; }
        internal ConfigFileReplace.SecretFreeBackup? RollbackBackup { get; init; }
        // the receiver-kept and imported values the written file must hold
        internal IReadOnlyList<(string Key, object? Value)> Expected { get; init; } = Array.Empty<(string, object?)>();
        public override string ToString() => (Create ? "create " : "replace ") + Name;
    }

    // an imported secret's new file: create-only, owner-only
    internal sealed class TransferSecretWrite
    {
        internal TransferSecretKind Kind { get; init; }
        internal string FileName { get; init; } = string.Empty;
        internal string FullPath { get; init; } = string.Empty;
        internal string Value { get; init; } = string.Empty;
        internal byte[] Bytes { get; init; } = Array.Empty<byte>();
        public override string ToString() => "secret file " + Kind;
    }

    // an earlier secret file of Campertron's that a successful import supersedes: removed only when nothing can still need it
    internal sealed class TransferOldSecret
    {
        internal TransferSecretKind Kind { get; init; }
        internal string FullPath { get; init; } = string.Empty;
        // its metadata as the import's build read it (never its content)
        internal string Facts { get; init; } = string.Empty;
        public override string ToString() => "earlier secret file " + Kind;
    }

    // What an apply runs (never shown): the folder, the package and choices it was built from, the transaction's identifier,
    // the writes in their order, the secret files, the backup set's files and the facts the read-back compares
    internal sealed class TransferPlan
    {
        internal string ConfigPath { get; init; } = string.Empty;
        internal TransferPackage Package { get; init; } = null!;
        internal TransferImportChoices Choices { get; init; } = TransferImportChoices.Everything;
        internal SearchPlace? Place { get; init; }
        internal string TransactionId { get; init; } = string.Empty;
        internal IReadOnlyList<TransferSecretWrite> Secrets { get; init; } = Array.Empty<TransferSecretWrite>();
        internal IReadOnlyList<TransferWrite> Writes { get; init; } = Array.Empty<TransferWrite>();
        internal IReadOnlyList<(string Name, byte[] Bytes)> BackupFiles { get; init; } = Array.Empty<(string, byte[])>();
        internal string BackupContents { get; init; } = string.Empty;
        internal IReadOnlyList<TransferOldSecret> OldSecrets { get; init; } = Array.Empty<TransferOldSecret>();
        internal IReadOnlyList<string> ReceiverSearches { get; init; } = Array.Empty<string>();
        internal IReadOnlyList<(string Name, string Facts)> Untouched { get; init; } = Array.Empty<(string, string)>();
        internal OutputType? ResultOutput { get; init; }
        public override string ToString() => $"TransferPlan: {Writes.Count} writes, {Secrets.Count} secret files";
    }

    // The import's candidate state and preview (D132, D134; v0.4.0-settings-transfer.md §Candidate-state construction,
    // §Receiver-kept keys, §Text-preserving YAML edits, §Validation, §outputTo gate, §Preview). Everything is built in memory from
    // a fresh read of the receiver's saved settings folder (never an editor's unsaved text) and the package; nothing is written:
    //  - the folder as it would be after the apply: for each name the package touches, the package's text with this computer's
    //    own values put back by text-range edits (YamlDocument; never a serialized model), else the receiver's present file;
    //    files absent from the package are not touched, and no search file is ever deleted. A search file is added or replaced
    //    only by its exact name; a name that differs from an existing entry in case only refuses the package. No example search
    //    is created;
    //  - the secrets: the SMTP password and the token each to a new owner-only file of Campertron's named after the
    //    transaction (Email.password.<id>, Ntfy.token.<id>), its path as smtpPasswordFile or tokenFile; never to smtpPassword;
    //    the topic into Ntfy.yaml only if the resulting file is owner-only, else refused alone;
    //  - the outputTo gate: a change to Email or Ntfy only when the monitoring start's own checks (MonitoringStartErrors,
    //    MonitoringSearchErrors) pass on the candidate (ConfigCandidate), else excluded and this computer's kept; never another
    //    outputTo chosen; nothing started;
    //  - each written text through the bounded loader and the validation an editor's Save applies, with the resulting outputTo;
    //  - the backup set's secret-free texts and the rolling backups' (D111, D112), refused when they can't be made.
    internal static class TransferImport
    {
        // the keys whose values belong to one computer (§Receiver-kept keys) and the secrets' references
        private const string LastRefreshKey = "lastRidbDataRefresh";
        private const string OutputToKey = "outputTo";
        private const string TopicKey = "topic";
        // their values are never shown in a preview's lines
        private static readonly HashSet<string> MaskedKeys = new(StringComparer.Ordinal) { LastRefreshKey, SmtpPassword.EmailYamlKey, SmtpPassword.FileKey, NtfyToken.FileKey, TopicKey };

        internal const string PasswordFilePrefix = "Email.password.";
        internal const string TokenFilePrefix = "Ntfy.token.";

        // the transaction's identifier: 128 random bits as 32 lower-case hexadecimal digits (RandomNumberGenerator), never a
        // time; replaceable for the tests' collision checks
        internal static Func<string> NewTransactionId = () => RandomNumberGenerator.GetHexString(32, lowercase: true);

        // the key of this process's revisions: a preview's revision is a keyed digest, so it reveals nothing of what it binds
        private static readonly byte[] RevisionKey = RandomNumberGenerator.GetBytes(32);
        private static readonly string CanonicalId = new string('0', 32);

        // whether a name is a secret file of Campertron's transfer: "Email.password.<32 hex>" or "Ntfy.token.<32 hex>"
        internal static bool IsTransferSecretName(string Name, TransferSecretKind Kind)
        {
            string Prefix = Kind == TransferSecretKind.SmtpPassword ? PasswordFilePrefix : TokenFilePrefix;
            return Name.Length == Prefix.Length + 32 && Name.StartsWith(Prefix, StringComparison.Ordinal)
                && Name.Substring(Prefix.Length).All(C => (C >= '0' && C <= '9') || (C >= 'a' && C <= 'f'));
        }

        // The preview of importing Package into the settings folder ConfigPath (its caller's: the transfer core finds no settings
        // folder by itself). Place names where the host's user edits the search files in the readiness checks' words
        internal static TransferPreview Preview(string ConfigPath, TransferPackage Package, TransferImportChoices? Choices = null, SearchPlace? Place = null)
        {
            ArgumentNullException.ThrowIfNull(ConfigPath);
            ArgumentNullException.ThrowIfNull(Package);
            string Id = NewTransactionId();
            if (Id.Length != 32 || !Id.All(C => (C >= '0' && C <= '9') || (C >= 'a' && C <= 'f')))
            {
                throw new InvalidOperationException("A transaction's identifier is 32 lower-case hexadecimal digits.");
            }
            return Build(ConfigPath, Package, Choices ?? TransferImportChoices.Everything, Id, Place, DateTime.Now);
        }

        // ---- the receiver ----

        // a settings file of the receiver as read for the candidate: whether it exists, its text and bytes as the safe writers
        // read it, its settings as the loader reads them, and whether either failed
        private sealed class ReceiverFile
        {
            internal string Name = string.Empty;
            internal string FullPath = string.Empty;
            internal bool Exists;
            internal ConfigFileReplace.SettingsFile? Read;
            internal bool Unreadable;
            internal object? Model;
            internal bool Unloadable;
        }

        private static ReceiverFile ReadReceiver<T>(string Folder, string Name) where T : class
        {
            var File_ = new ReceiverFile { Name = Name, FullPath = Path.Join(Folder, Name) };
            File_.Exists = TransferProtection.Exists(File_.FullPath);
            if (!File_.Exists)
            {
                return File_;
            }
            try
            {
                File_.Read = ConfigFileReplace.Read(File_.FullPath);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                File_.Unreadable = true;
                return File_;
            }
            File_.Model = Load<T>(File_.Read.Text, out bool Refused);
            File_.Unloadable = Refused || File_.Model == null;
            return File_;
        }

        // the settings of a text as every loader reads them (the bounded loader), or null and Refused where it refuses them
        private static T? Load<T>(string Text, out bool Refused) where T : class
        {
            Refused = false;
            try
            {
                return Yaml.LoadSettingsText<T>(Text);
            }
            catch (Exception ex) when (ex is YamlException || ex is Yaml.SettingsLimitException)
            {
                Refused = true;
                return null;
            }
        }

        // a file's identity, content and permissions for the revision: never a secret file's (MetadataFacts is for those)
        private static string Facts(string FullPath)
        {
            try
            {
                if (Directory.Exists(FullPath))
                {
                    return "folder";
                }
                var Info = new FileInfo(FullPath);
                string? Link = Info.LinkTarget;
                if (!Info.Exists)
                {
                    return Link != null ? "dangling link " + Link : "absent";
                }
                // a file that reports no length (also a FIFO or a device, which could block) is never opened
                string Hash = Info.Length > 0 && Info.Length <= ConfigFileReplace.MaxBytes ? System.Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(FullPath))) : "unread";
                string Protection = OperatingSystem.IsWindows()
                    ? new FileInfo(FullPath).GetAccessControl(System.Security.AccessControl.AccessControlSections.Access).GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.Access)
                    : File.GetUnixFileMode(FullPath).ToString();
                return (Link != null ? "link " + Link + " " : "file ") + Info.Length + " " + Info.LastWriteTimeUtc.Ticks + " " + Info.Attributes + " " + Protection + " " + Hash;
            }
            catch (Exception ex) when (TransferProtection.IsFileSystemFailure(ex))
            {
                return "unreadable " + ex.GetType().Name;
            }
        }

        // ---- the build ----

        // The candidate and its preview for a transaction identifier: deterministic for the same folder, package, choices and
        // identifier, so an apply builds it again under its lock and compares the revision (the stale check)
        internal static TransferPreview Build(string ConfigPath, TransferPackage Package, TransferImportChoices Choices, string TransactionId, SearchPlace? Place, DateTime Now)
        {
            string Folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ConfigPath));
            var Refusals = new List<TransferImportFailure>();
            var Revision = new StringBuilder("campertron-transfer-preview 1\n");
            Revision.Append("package ").Append(Package.FormatVersion).Append(' ').Append(Package.ExportedBy).Append(' ').Append(Package.Encrypted).Append('\n');
            foreach (TransferEntry Entry in Package.Entries)
            {
                Revision.Append("entry ").Append(TransferFormat.KindText(Entry.Kind)).Append(' ').Append(Entry.Name).Append(' ').Append(Entry.Sha256).Append('\n');
            }
            Revision.Append("left-out ").Append(string.Join("/", Choices.LeftOut.OrderBy(N => N, StringComparer.Ordinal))).Append('\n');
            Revision.Append("secrets-left-out ").Append(string.Join("/", Choices.SecretsLeftOut.Distinct().Order())).Append('\n');
            var NoLines = Array.Empty<ConfigPreviewLine>();
            if (!Directory.Exists(Folder))
            {
                Refusals.Add(TransferImportWording.Preview(TransferImportProblem.FolderMissing));
                return Preview_(Revision, Refusals, Package, new List<TransferPreviewFile>(), new List<string>(), new List<(string, string)>(), new List<TransferPreviewSecret>(),
                    new TransferPreviewOutputTo(null, null, null, TransferOutputToDecision.NotCarried, Array.Empty<ConfigDiagnostic>(), Array.Empty<ConfigDiagnostic>()), new List<ConfigDiagnostic>(), new List<string>(), false,
                    new TransferPlan { ConfigPath = Folder, Package = Package, Choices = Choices, Place = Place, TransactionId = TransactionId });
            }

            // the entries chosen
            var Chosen = Package.Entries.Where(E => !E.IsSecret && !Choices.LeftOut.Contains(E.Name, StringComparer.Ordinal)).ToList();
            TransferEntry? Of(TransferEntryKind Kind) => Chosen.FirstOrDefault(E => E.Kind == Kind);
            TransferEntry? GeneralEntry = Of(TransferEntryKind.General), EmailEntry = Of(TransferEntryKind.Email), NtfyEntry = Of(TransferEntryKind.Ntfy);
            List<TransferEntry> SearchEntries = Chosen.Where(E => E.Kind == TransferEntryKind.Search)
                .OrderBy(E => E.Name, StringComparer.OrdinalIgnoreCase).ThenBy(E => E.Name, StringComparer.Ordinal).ToList();
            if (Chosen.Count == 0)
            {
                Refusals.Add(TransferImportWording.Preview(TransferImportProblem.NothingChosen));
            }

            // the receiver, read fresh: its saved files only
            ReceiverFile General = ReadReceiver<GeneralConfig>(Folder, ConfigFiles.GeneralFileName);
            ReceiverFile Email = ReadReceiver<EmailConfig>(Folder, ConfigFiles.EmailFileName);
            ReceiverFile Ntfy = ReadReceiver<NtfyConfig>(Folder, ConfigFiles.NtfyFileName);
            var EntryNames = Directory.EnumerateFileSystemEntries(Folder).Select(E => Path.GetFileName(E)).ToList();
            List<string> ReceiverSearches = ConfigFiles.SortedSearchFileNames(Folder);
            var Facts_ = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (string Name in new[] { ConfigFiles.GeneralFileName, ConfigFiles.EmailFileName, ConfigFiles.NtfyFileName }.Concat(ReceiverSearches).Concat(SearchEntries.Select(E => E.Name)))
            {
                Facts_[Name] = Facts(Path.Join(Folder, Name));
            }
            // the entries a search name meets ignoring case (a case-only conflict, or a name taken since the preview)
            foreach (TransferEntry Search in SearchEntries)
            {
                Facts_["case " + Search.Name] = string.Join("/", EntryNames.Where(N => string.Equals(N, Search.Name, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal));
            }
            UnixFileMode? NtfyMode = Ntfy.Read?.Mode;
            Facts_["topic-protection"] = TransferProtection.TopicFacts(Folder, Ntfy.Exists, NtfyMode);
            // the receiver's settings a candidate needs must be readable, and loadable for their own values
            foreach ((ReceiverFile File_, TransferEntry? Entry) in new[] { (General, GeneralEntry), (Email, EmailEntry), (Ntfy, NtfyEntry) })
            {
                if (Entry == null || !File_.Exists)
                {
                    continue;
                }
                if (File_.Unreadable)
                {
                    Refusals.Add(TransferImportWording.Preview(TransferImportProblem.ReceiverUnreadable, File_.Name));
                }
                else if (File_.Unloadable)
                {
                    Refusals.Add(TransferImportWording.Preview(TransferImportProblem.ReceiverUnloadable, File_.Name));
                }
            }
            var GeneralModel = General.Model as GeneralConfig;
            var EmailModel = Email.Model as EmailConfig;
            var NtfyModel = Ntfy.Model as NtfyConfig;

            // the search files: added, or replaced by their exact name
            var SearchReads = new Dictionary<string, ConfigFileReplace.SettingsFile?>(StringComparer.Ordinal);
            foreach (TransferEntry Search in SearchEntries)
            {
                bool Exact = EntryNames.Contains(Search.Name, StringComparer.Ordinal);
                if (!Exact && EntryNames.Any(N => string.Equals(N, Search.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    Refusals.Add(TransferImportWording.Preview(TransferImportProblem.CaseOnlyConflict, Search.Name));
                    continue;
                }
                if (!Exact)
                {
                    SearchReads[Search.Name] = null;
                    continue;
                }
                try
                {
                    SearchReads[Search.Name] = ConfigFileReplace.Read(Path.Join(Folder, Search.Name));
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
                {
                    Refusals.Add(TransferImportWording.Preview(TransferImportProblem.ReceiverUnreadable, Search.Name));
                }
            }

            // the secrets' decisions
            var SecretValues = new List<string>();
            var Secrets = new List<TransferPreviewSecret>();
            var SecretPlans = new List<TransferSecretWrite>();
            string? ImportedTopic = null;
            foreach (TransferEntry Secret in Package.Entries.Where(E => E.IsSecret))
            {
                TransferSecretKind Kind = TransferFormat.SecretOf(Secret.Kind);
                SecretValues.Add(Secret.Text);
                bool FileChosen = TransferFormat.OwnerOf(Kind) == TransferEntryKind.Email ? EmailEntry != null : NtfyEntry != null;
                TransferImportSecretDecision Decision =
                    Choices.SecretsLeftOut.Contains(Kind) ? TransferImportSecretDecision.LeftOut
                    : !FileChosen ? TransferImportSecretDecision.FileLeftOut
                    : Kind == TransferSecretKind.NtfyToken && !NtfyToken.IsValidToken(Secret.Text) ? TransferImportSecretDecision.RefusedUnusable
                    : Kind == TransferSecretKind.NtfyTopic && !NtfyEndpoint.IsValidTopic(Secret.Text) ? TransferImportSecretDecision.RefusedUnusable
                    : Kind == TransferSecretKind.NtfyTopic && !TransferProtection.TopicAllowed(Folder, Ntfy.Exists, NtfyMode) ? TransferImportSecretDecision.RefusedReadableByOthers
                    : TransferImportSecretDecision.Imported;
                TransferImportSecretSource Before = Kind switch
                {
                    TransferSecretKind.SmtpPassword => EmailModel?.SmtpPasswordFileWritten == true ? TransferImportSecretSource.File
                        : !string.IsNullOrEmpty(EmailModel?.SmtpPassword) ? TransferImportSecretSource.VariableOrPlainText : TransferImportSecretSource.VariableOrNone,
                    TransferSecretKind.NtfyToken => NtfyModel?.TokenFileWritten == true ? TransferImportSecretSource.File : TransferImportSecretSource.VariableOrNone,
                    _ => TransferImportSecretSource.Topic
                };
                Secrets.Add(new TransferPreviewSecret(Kind, Decision, Before));
                Revision.Append("secret ").Append(Kind).Append(' ').Append(Decision).Append('\n');
                if (Decision != TransferImportSecretDecision.Imported)
                {
                    continue;
                }
                if (Kind == TransferSecretKind.NtfyTopic)
                {
                    ImportedTopic = Secret.Text;
                    continue;
                }
                string FileName = (Kind == TransferSecretKind.SmtpPassword ? PasswordFilePrefix : TokenFilePrefix) + TransactionId;
                SecretPlans.Add(new TransferSecretWrite
                {
                    Kind = Kind,
                    FileName = FileName,
                    FullPath = Path.Join(Folder, FileName),
                    Value = Secret.Text,
                    Bytes = TransferFormat.StrictUtf8.GetBytes(Secret.Text)
                });
            }
            string? NewPasswordFile = SecretPlans.FirstOrDefault(S => S.Kind == TransferSecretKind.SmtpPassword)?.FullPath;
            string? NewTokenFile = SecretPlans.FirstOrDefault(S => S.Kind == TransferSecretKind.NtfyToken)?.FullPath;

            // the receiver's own secret values and any the package's texts hold: never in a backup, a preview or a line
            var Known = new List<string>(SecretValues);
            foreach (string? Value in new[] { EmailModel?.SmtpPassword, NtfyModel?.Topic })
            {
                if (!string.IsNullOrEmpty(Value))
                {
                    Known.Add(Value);
                }
            }
            if (EmailEntry != null && Load<EmailConfig>(EmailEntry.Text, out _)?.SmtpPassword is string PackagePassword && PackagePassword.Length > 0)
            {
                Known.Add(PackagePassword);
            }
            if (NtfyEntry != null && Load<NtfyConfig>(NtfyEntry.Text, out _)?.Topic is string PackageTopic && PackageTopic.Length > 0)
            {
                Known.Add(PackageTopic);
            }

            // the settings texts with this computer's own values (§Receiver-kept keys)
            var Kept = new List<(string File, string Key)>();
            string? GeneralText = null, EmailText = null, NtfyText = null;
            var GeneralExpected = new List<(string, object?)>();
            var EmailExpected = new List<(string, object?)>();
            var NtfyExpected = new List<(string, object?)>();
            if (GeneralEntry != null && !Refusals.Any(R => R.Name == ConfigFiles.GeneralFileName))
            {
                var Wanted = new List<(string, object?)> { (LastRefreshKey, GeneralModel?.LastRidbDataRefresh) };
                GeneralText = WithReceiverValues<GeneralConfig>(GeneralEntry, Wanted, Refusals);
                GeneralExpected.AddRange(Wanted);
                if (GeneralModel?.LastRidbDataRefresh != null)
                {
                    Kept.Add((ConfigFiles.GeneralFileName, LastRefreshKey));
                }
            }
            if (EmailEntry != null && !Refusals.Any(R => R.Name == ConfigFiles.EmailFileName))
            {
                object? PasswordFile = NewPasswordFile ?? (EmailModel?.SmtpPasswordFileWritten == true ? EmailModel.SmtpPasswordFile ?? string.Empty : null);
                var Wanted = new List<(string, object?)> { (SmtpPassword.EmailYamlKey, EmailModel?.SmtpPassword), (SmtpPassword.FileKey, PasswordFile) };
                EmailText = WithReceiverValues<EmailConfig>(EmailEntry, Wanted, Refusals);
                EmailExpected.AddRange(Wanted);
                if (EmailModel?.SmtpPassword != null)
                {
                    Kept.Add((ConfigFiles.EmailFileName, SmtpPassword.EmailYamlKey));
                }
                if (NewPasswordFile == null && EmailModel?.SmtpPasswordFileWritten == true)
                {
                    Kept.Add((ConfigFiles.EmailFileName, SmtpPassword.FileKey));
                }
            }
            if (NtfyEntry != null && !Refusals.Any(R => R.Name == ConfigFiles.NtfyFileName))
            {
                object? TokenFile = NewTokenFile ?? (NtfyModel?.TokenFileWritten == true ? NtfyModel.TokenFile ?? string.Empty : null);
                var Wanted = new List<(string, object?)> { (TopicKey, ImportedTopic ?? NtfyModel?.Topic), (NtfyToken.FileKey, TokenFile) };
                NtfyText = WithReceiverValues<NtfyConfig>(NtfyEntry, Wanted, Refusals);
                NtfyExpected.AddRange(Wanted);
                if (ImportedTopic == null && NtfyModel?.Topic != null)
                {
                    Kept.Add((ConfigFiles.NtfyFileName, TopicKey));
                }
                if (NewTokenFile == null && NtfyModel?.TokenFileWritten == true)
                {
                    Kept.Add((ConfigFiles.NtfyFileName, NtfyToken.FileKey));
                }
            }

            // the outputTo gate on the candidate (D71, D73): the monitoring start's own checks, nothing copied
            OutputType? ReceiverOutput = !General.Exists ? OutputType.Console : GeneralModel?.OutputTo ?? LenientOutput(General.FullPath, Now);
            OutputType? PackageOutput = GeneralText != null ? Load<GeneralConfig>(GeneralText, out _)?.OutputTo : null;
            var GateFiles = new Dictionary<string, string?>(StringComparer.Ordinal);
            if (EmailText != null)
            {
                GateFiles[ConfigFiles.EmailFileName] = EmailText;
            }
            if (NtfyText != null)
            {
                GateFiles[ConfigFiles.NtfyFileName] = NtfyText;
            }
            foreach (TransferEntry Search in SearchEntries)
            {
                GateFiles[Search.Name] = Search.Text;
            }
            var Candidate = new ConfigCandidate(GateFiles);
            List<ConfigDiagnostic> Gate(OutputType Output) =>
                ConfigValidation.MonitoringStartErrors(Folder, Output, Now, Candidate).Concat(ConfigValidation.MonitoringSearchErrors(Folder, Output, null, Place, Candidate)).ToList();
            static bool Monitors(OutputType? Output) => Output == OutputType.Email || Output == OutputType.Ntfy;
            TransferOutputToDecision OutputDecision;
            OutputType? ResultOutput;
            List<ConfigDiagnostic> GateFindings = new List<ConfigDiagnostic>();
            if (PackageOutput == null)
            {
                OutputDecision = TransferOutputToDecision.NotCarried;
                ResultOutput = ReceiverOutput;
            }
            else if (PackageOutput == ReceiverOutput)
            {
                OutputDecision = TransferOutputToDecision.Unchanged;
                ResultOutput = ReceiverOutput;
            }
            else if (!Monitors(PackageOutput) || (GateFindings = Gate(PackageOutput.Value)).Count == 0)
            {
                OutputDecision = TransferOutputToDecision.Accepted;
                ResultOutput = PackageOutput;
            }
            else
            {
                // the change can't be applied: the rest can, with this computer's outputTo (never another one chosen)
                OutputDecision = TransferOutputToDecision.Excluded;
                ResultOutput = ReceiverOutput ?? OutputType.Console;
                var Wanted = new List<(string, object?)> { (OutputToKey, ResultOutput.Value) };
                GeneralText = WithReceiverValues<GeneralConfig>(GeneralEntry!, GeneralExpected.Concat(Wanted).ToList(), Refusals);
                GeneralExpected.AddRange(Wanted);
                Kept.Add((ConfigFiles.GeneralFileName, OutputToKey));
            }
            // this computer's outputTo, kept (unchanged, not carried, or after an excluded change), when it is Email or Ntfy: what
            // the same checks find on the candidate is a warning
            List<ConfigDiagnostic> ResultFindings = OutputDecision != TransferOutputToDecision.Accepted && Monitors(ResultOutput) ? Gate(ResultOutput!.Value) : new List<ConfigDiagnostic>();
            Revision.Append("output ").Append(OutputDecision).Append(' ').Append(ResultOutput?.ToString() ?? "unknown").Append('\n');

            // the candidate's writes, each validated as an editor's Save validates it, with the resulting outputTo
            var Files = new List<TransferPreviewFile>();
            var Writes = new List<TransferWrite>();
            var Findings = new List<ConfigDiagnostic>();
            var Warnings = new List<string>();
            bool NtfyIncomplete = false;
            var BackupFiles = new List<(string, byte[])>();
            string[] Secret_ = Known.Distinct().ToArray();
            void Consider(TransferEntry Entry, ConfigFileKind Kind, string? Text, ReceiverFile? Receiver, ConfigFileReplace.SettingsFile? Existing, bool Exists, IReadOnlyList<(string, object?)> Expected)
            {
                if (Text == null)
                {
                    return;
                }
                string FullPath = Path.Join(Folder, Entry.Name);
                ConfigFileReplace.SettingsFile? Original = Receiver?.Read ?? Existing;
                bool Create = !Exists;
                if (!Create && Original != null && string.Equals(Original.Text, Text, StringComparison.Ordinal))
                {
                    Files.Add(new TransferPreviewFile(Entry.Name, Kind == ConfigFileKind.Search, TransferImportFileChange.Unchanged, NoLines));
                    return;
                }
                if (!Create && Original == null)
                {
                    // refused above (unreadable)
                    return;
                }
                List<ConfigDiagnostic> Found = Kind switch
                {
                    ConfigFileKind.General => ConfigValidation.ValidateGeneral(Text, Now, Entry.Name),
                    ConfigFileKind.Email => ConfigValidation.ValidateEmail(Text, ResultOutput == OutputType.Email, Entry.Name),
                    ConfigFileKind.Ntfy => ConfigValidation.ValidateNtfy(Text, ResultOutput == OutputType.Ntfy, Entry.Name),
                    _ => ConfigValidation.ValidateSearch(Text, Entry.Name, Now, ResultOutput)
                };
                foreach (ConfigDiagnostic D in Found)
                {
                    if (D.Severity != ConfigSeverity.Error)
                    {
                        Findings.Add(D);
                    }
                    else if (Kind == ConfigFileKind.Ntfy && D.Code == ConfigCode.NtfyTopicMissing)
                    {
                        // a kept key absent where the result needs it: what stays incomplete, not a refusal (§Receiver-kept keys)
                        NtfyIncomplete = true;
                        Findings.Add(D);
                    }
                    else
                    {
                        Refusals.Add(TransferImportWording.Preview(TransferImportProblem.PackageFileRefused, Entry.Name, D.Message));
                    }
                }
                // a password, token or topic never travels into a settings text outside its own setting
                if (Kind != ConfigFileKind.Search && HoldsSecretOutsideItsSetting(Text, SecretValues))
                {
                    Refusals.Add(TransferImportWording.Preview(TransferImportProblem.PackageFileHoldsSecret, Entry.Name));
                }
                ConfigFileReplace.SecretFreeBackup? Forward = null, Back = null;
                if (Kind == ConfigFileKind.Email || Kind == ConfigFileKind.Ntfy)
                {
                    Back = Create ? null : SecretFree(Kind, Text, Secret_, Entry.Name, Refusals);
                    Forward = Create ? null : SecretFree(Kind, Original!.Text, Secret_, Entry.Name, Refusals);
                }
                if (!Create)
                {
                    BackupFiles.Add((Entry.Name, Forward != null ? ConfigFileReplace.StrictUtf8.GetBytes(Forward.Text) : Original!.Bytes));
                }
                IReadOnlyList<ConfigPreviewLine> Lines = Kind == ConfigFileKind.Search ? NoLines
                    : ConfigEditPreview.Build(Create ? string.Empty : Original!.Text, Text, Create ? Array.Empty<(int, int)>() : MaskedRanges(Original!.Text), MaskedRanges(Text),
                        Secret_.Concat(new[] { Folder }).ToList());
                Files.Add(new TransferPreviewFile(Entry.Name, Kind == ConfigFileKind.Search, Create ? TransferImportFileChange.Created : TransferImportFileChange.Replaced, Lines));
                Writes.Add(new TransferWrite
                {
                    Name = Entry.Name,
                    FullPath = FullPath,
                    Kind = Kind,
                    Create = Create,
                    Text = Text,
                    Bytes = ConfigFileReplace.Encode(Text, Entry.Name),
                    Original = Create ? null : Original,
                    CreateMode = Create && (Kind == ConfigFileKind.Email || Kind == ConfigFileKind.Ntfy) ? TransferProtection.OwnerOnlyFile : null,
                    ForwardBackup = Forward,
                    RollbackBackup = Back,
                    Expected = Expected
                });
            }
            // the write order (§Write order): the search files by name, then Ntfy.yaml, Email.yaml and General.yaml last
            foreach (TransferEntry Search in SearchEntries.Where(S => SearchReads.ContainsKey(S.Name)))
            {
                Consider(Search, ConfigFileKind.Search, Search.Text, null, SearchReads[Search.Name], SearchReads[Search.Name] != null, Array.Empty<(string, object?)>());
            }
            if (NtfyEntry != null)
            {
                Consider(NtfyEntry, ConfigFileKind.Ntfy, NtfyText, Ntfy, null, Ntfy.Exists, NtfyExpected);
            }
            if (EmailEntry != null)
            {
                Consider(EmailEntry, ConfigFileKind.Email, EmailText, Email, null, Email.Exists, EmailExpected);
            }
            if (GeneralEntry != null)
            {
                Consider(GeneralEntry, ConfigFileKind.General, GeneralText, General, null, General.Exists, GeneralExpected);
            }
            // a secret file is written only for a settings file that is written and names it
            if (SecretPlans.Any(S => S.Kind == TransferSecretKind.SmtpPassword) && !Writes.Any(W => W.Kind == ConfigFileKind.Email))
            {
                SecretPlans.RemoveAll(S => S.Kind == TransferSecretKind.SmtpPassword);
            }
            if (SecretPlans.Any(S => S.Kind == TransferSecretKind.NtfyToken) && !Writes.Any(W => W.Kind == ConfigFileKind.Ntfy))
            {
                SecretPlans.RemoveAll(S => S.Kind == TransferSecretKind.NtfyToken);
            }
            foreach (TransferEntry Left in Package.Entries.Where(E => !E.IsSecret && Choices.LeftOut.Contains(E.Name, StringComparer.Ordinal)))
            {
                Files.Add(new TransferPreviewFile(Left.Name, Left.Kind == TransferEntryKind.Search, TransferImportFileChange.LeftOut, NoLines));
            }
            if (Chosen.Count > 0 && Writes.Count == 0 && Refusals.Count == 0)
            {
                Refusals.Add(TransferImportWording.Preview(TransferImportProblem.NothingToChange));
            }
            if (NtfyIncomplete)
            {
                Warnings.Add(TransferImportWording.NtfyIncomplete);
            }
            if (SecretPlans.Count > 0)
            {
                Warnings.Add(TransferImportWording.PathIsThisComputers);
            }

            // the earlier secret files of Campertron's the import supersedes: only a transaction file this folder holds
            var OldSecrets = new List<TransferOldSecret>();
            foreach (TransferSecretWrite New in SecretPlans)
            {
                string? Previous = New.Kind == TransferSecretKind.SmtpPassword
                    ? (EmailModel?.SmtpPasswordFileWritten == true ? EmailModel.SmtpPasswordFile : null)
                    : (NtfyModel?.TokenFileWritten == true ? NtfyModel.TokenFile : null);
                if (Previous != null && Path.IsPathFullyQualified(Previous)
                    && string.Equals(Path.GetDirectoryName(Path.GetFullPath(Previous)), Folder, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                    && IsTransferSecretName(Path.GetFileName(Previous), New.Kind))
                {
                    OldSecrets.Add(new TransferOldSecret { Kind = New.Kind, FullPath = Path.GetFullPath(Previous), Facts = TransferProtection.MetadataFacts(Path.GetFullPath(Previous)) });
                }
            }

            // the files the apply must not change (metadata only, never their content): the web UI's own
            var Untouched = new List<(string, string)>
            {
                (ConfigFiles.WebFileName, TransferProtection.MetadataFacts(Path.Join(Folder, ConfigFiles.WebFileName))),
                (WebAccess.WebPasswordFile.FileName, TransferProtection.MetadataFacts(Path.Join(Folder, WebAccess.WebPasswordFile.FileName)))
            };

            // the revision: the receiver's facts, the decisions and the candidate's bytes (the transaction's identifier left out,
            // so the same inputs give the same revision)
            foreach (KeyValuePair<string, string> Fact in Facts_)
            {
                Revision.Append("fact ").Append(Fact.Key).Append(' ').Append(Fact.Value).Append('\n');
            }
            foreach (TransferOldSecret Old in OldSecrets)
            {
                Revision.Append("old ").Append(Old.Kind).Append(' ').Append(Old.Facts).Append('\n');
            }
            foreach ((string Name, string Fact) in Untouched)
            {
                Revision.Append("untouched ").Append(Name).Append(' ').Append(Fact).Append('\n');
            }
            foreach (TransferWrite Write in Writes)
            {
                Revision.Append("write ").Append(Write.Create ? "create " : "replace ").Append(Write.Name).Append(' ')
                    .Append(System.Convert.ToHexStringLower(SHA256.HashData(TransferFormat.StrictUtf8.GetBytes(Write.Text.Replace(TransactionId, CanonicalId, StringComparison.Ordinal))))).Append('\n');
            }
            foreach (TransferSecretWrite Secret in SecretPlans)
            {
                Revision.Append("secret-file ").Append(Secret.Kind).Append(' ').Append(Secret.FileName.Replace(TransactionId, CanonicalId, StringComparison.Ordinal)).Append('\n');
            }
            foreach (TransferImportFailure Refusal in Refusals)
            {
                Revision.Append("refused ").Append(Refusal.Problem).Append(' ').Append(Refusal.Name).Append('\n');
            }
            var Plan = new TransferPlan
            {
                ConfigPath = Folder,
                Package = Package,
                Choices = Choices,
                Place = Place,
                TransactionId = TransactionId,
                Secrets = SecretPlans,
                Writes = Writes,
                BackupFiles = BackupFiles,
                BackupContents = TransferBackupSet.Contents(Writes.Where(W => !W.Create).Select(W => W.Name).ToList(),
                    Writes.Where(W => W.Create).Select(W => W.Name).Concat(SecretPlans.Select(S => S.FileName)).ToList(), TransferFormat.CurrentExportedBy(), DateTime.UtcNow),
                OldSecrets = OldSecrets,
                ReceiverSearches = ReceiverSearches,
                Untouched = Untouched,
                ResultOutput = ResultOutput
            };
            List<string> Preserved = ReceiverSearches.Where(N => !SearchEntries.Any(E => string.Equals(E.Name, N, StringComparison.Ordinal))).ToList();
            var OutputTo = new TransferPreviewOutputTo(ReceiverOutput, PackageOutput, ResultOutput, OutputDecision,
                OutputDecision == TransferOutputToDecision.Excluded ? GateFindings : new List<ConfigDiagnostic>(), ResultFindings);
            return Preview_(Revision, Refusals, Package, Files, Preserved, Kept, Secrets, OutputTo, Findings, Warnings, NtfyIncomplete, Plan);
        }

        private static TransferPreview Preview_(StringBuilder Revision, List<TransferImportFailure> Refusals, TransferPackage Package, List<TransferPreviewFile> Files,
            List<string> Preserved, List<(string, string)> Kept, List<TransferPreviewSecret> Secrets, TransferPreviewOutputTo OutputTo, List<ConfigDiagnostic> Findings,
            List<string> Warnings, bool NtfyIncomplete, TransferPlan Plan)
        {
            string Digest = System.Convert.ToHexStringLower(HMACSHA256.HashData(RevisionKey, TransferFormat.StrictUtf8.GetBytes(Revision.ToString())));
            // a refusal's words once each
            List<TransferImportFailure> Distinct = Refusals.GroupBy(R => R.Message, StringComparer.Ordinal).Select(G => G.First()).ToList();
            return new TransferPreview(Digest, Distinct, TransferImportWording.PackageWords(Package), Files, Preserved, Kept, Secrets, OutputTo, Findings, Warnings, NtfyIncomplete, Plan);
        }

        // General.yaml's outputTo as the start's lenient reader reads it, for a General.yaml the safe writers can't read
        private static OutputType? LenientOutput(string GeneralPath, DateTime Now)
        {
            ConfigValidation.ValidateFile(GeneralPath, Now, null, out object? Settings);
            return (Settings as GeneralConfig)?.OutputTo;
        }

        // The package's text of a settings file with this computer's values put in by text-range edits: each key set to its
        // value (inserted when absent) or removed (value null), only where the text differs; the result must load as exactly
        // the intended settings (YamlDocument). Null, with the file's refusal, when the loader or the editor refuses it
        private static string? WithReceiverValues<T>(TransferEntry Entry, IReadOnlyList<(string Key, object? Value)> Wanted, List<TransferImportFailure> Refusals) where T : class
        {
            T? Model = Load<T>(Entry.Text, out bool Refused);
            T? Intended = Load<T>(Entry.Text, out _);
            if (Model == null || Intended == null || Refused)
            {
                Refusals.Add(TransferImportWording.Preview(TransferImportProblem.PackageFileUnloadable, Entry.Name));
                return null;
            }
            IReadOnlyDictionary<string, System.Reflection.PropertyInfo> Keys = YamlDocument.ModelKeys(typeof(T));
            YamlDocumentAnalysis Analysis = YamlDocument.Analyze(Entry.Text);
            var Edits = new List<YamlDocumentEdit>();
            foreach ((string Key, object? Value) in Wanted)
            {
                System.Reflection.PropertyInfo Property = Keys[Key];
                bool Present = Analysis.Properties.Any(P => string.Equals(P.Key, Key, StringComparison.Ordinal));
                if (Value == null)
                {
                    if (Present)
                    {
                        Edits.Add(YamlDocumentEdit.RemoveProperty(Key));
                        Property.SetValue(Intended, null);
                    }
                    continue;
                }
                if (!Present || !ConfigEditSession<GeneralConfig>.SameValue(Property.PropertyType, Property.GetValue(Model), Value))
                {
                    Edits.Add(YamlDocumentEdit.SetScalar(Key, Value));
                    Property.SetValue(Intended, Value);
                }
            }
            if (Edits.Count == 0)
            {
                return Entry.Text;
            }
            YamlDocumentResult Edited = YamlDocument.Edit(Entry.Text, Edits, Intended);
            if (!Edited.Succeeded || Edited.Text == null)
            {
                Refusals.Add(TransferImportWording.Preview(TransferImportProblem.PackageFileNotEditable, Entry.Name));
                return null;
            }
            return Edited.Text;
        }

        // the value ranges of the keys a preview never shows
        private static IReadOnlyList<(int Start, int End)> MaskedRanges(string Text) =>
            YamlDocument.Analyze(Text).Properties.Where(P => MaskedKeys.Contains(P.Key)).Select(P => (P.ValueStart, P.ValueEnd)).ToList();

        // A settings text that holds a package's secret outside the settings the import itself decides (this computer's kept
        // values, the new secret files' paths, an imported topic): in a comment or another key, a carried password, token or
        // topic must not be (each kept and imported value is checked on its own, after the write)
        private static bool HoldsSecretOutsideItsSetting(string Text, IReadOnlyList<string> PackageSecrets)
        {
            string Outside = Text;
            foreach ((int Start, int End) in MaskedRanges(Text).OrderByDescending(R => R.Start))
            {
                Outside = Outside.Substring(0, Start) + "\0" + Outside.Substring(End);
            }
            return PackageSecrets.Any(Value => Value.Length > 0 && Outside.Contains(Value, StringComparison.Ordinal));
        }

        // D111, D112: a backup's text of Email.yaml or Ntfy.yaml without the plain-text password and the topic ('' in place of a
        // non-empty value), checked to hold no known secret anywhere; null, with the file's refusal, when that can't be made
        private static ConfigFileReplace.SecretFreeBackup? SecretFree(ConfigFileKind Kind, string Text, IReadOnlyList<string> Known, string Name, List<TransferImportFailure> Refusals)
        {
            string Key = Kind == ConfigFileKind.Email ? SmtpPassword.EmailYamlKey : TopicKey;
            string? Cleared = Kind == ConfigFileKind.Email ? Clear<EmailConfig>(Text, Key) : Clear<NtfyConfig>(Text, Key);
            Func<string, string?> Verify = Written => Cleared != null && string.Equals(Written, Cleared, StringComparison.Ordinal) && !Known.Any(V => V.Length > 0 && Written.Contains(V, StringComparison.Ordinal))
                ? null : "it would hold a password, token or topic";
            if (Cleared == null || Verify(Cleared) != null)
            {
                Refusals.Add(TransferImportWording.Preview(TransferImportProblem.BackupNotSecretFree, Name));
                return null;
            }
            return new ConfigFileReplace.SecretFreeBackup(Cleared, Verify, Kind == ConfigFileKind.Email ? "its plain-text password" : "its topic");
        }

        private static string? Clear<T>(string Text, string Key) where T : class
        {
            T? Model = Load<T>(Text, out _);
            if (Model == null)
            {
                return null;
            }
            System.Reflection.PropertyInfo Property = YamlDocument.ModelKeys(typeof(T))[Key];
            if (Property.GetValue(Model) is not string Value || Value.Length == 0)
            {
                return Text;
            }
            Property.SetValue(Model, string.Empty);
            YamlDocumentResult Edited = YamlDocument.Edit(Text, new[] { YamlDocumentEdit.SetScalar(Key, string.Empty) }, Model);
            return Edited.Succeeded ? Edited.Text : null;
        }
    }
}
