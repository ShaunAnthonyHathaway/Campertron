using CampertronLibrary.function.Base;

namespace CampertronLibrary.function.Transfer.Apply
{
    // why a preview can't be applied, or why an apply did not run or did not complete (v0.4.0-settings-transfer.md §Safe errors)
    internal enum TransferImportProblem
    {
        // a preview's: these name at most a settings or search file's name that passed the name rules
        FolderMissing,
        NothingChosen,
        NothingToChange,
        CaseOnlyConflict,
        ReceiverUnreadable,
        ReceiverUnloadable,
        ReceiverNotReplaceable,
        PackageFileUnloadable,
        PackageFileRefused,
        PackageFileNotEditable,
        PackageFileHoldsSecret,
        BackupNotSecretFree,
        // an apply refused before anything was written: these name nothing
        NotApplicable,
        NotConfirmed,
        AlreadyUsed,
        Cancelled,
        UnsavedChanges,
        RefreshRunning,
        MonitoringStarting,
        LockHeld,
        LockUnavailable,
        Stale,
        // an apply that failed after its lock
        FailedNothingChanged,
        FailedNotRestored
    }

    // A preview's refusal or an apply's problem: its kind and its fixed sentence, which holds at most a settings or search file's
    // name, a secret's kind, outputTo's value and the validation's or the readiness checks' own value-free words: never a value
    // of a settings file, a secret, the passphrase, a path, an exception's or the system's message (§Logging and redaction)
    internal sealed class TransferImportFailure
    {
        internal TransferImportFailure(TransferImportProblem Problem, string Message, string? Name = null)
        {
            this.Problem = Problem;
            this.Message = Message;
            this.Name = Name;
        }
        internal TransferImportProblem Problem { get; }
        internal string Message { get; }
        internal string? Name { get; }
        public override string ToString() => Problem + ": " + Message;
    }

    // The import's fixed sentences (D134; §Safe errors, §Preview, §Interface parity: an adapter shows them and defines none). The
    // menus' and pages' own words are phases 17 to 19's.
    internal static class TransferImportWording
    {
        // ---- a preview's refusals ----

        internal static TransferImportFailure Preview(TransferImportProblem Problem, string? Name = null, string? Detail = null) => new TransferImportFailure(Problem, Problem switch
        {
            TransferImportProblem.FolderMissing => "The settings folder does not exist, so nothing can be imported into it.",
            TransferImportProblem.NothingChosen => "Nothing of this settings package is chosen to import.",
            TransferImportProblem.NothingToChange => "Nothing to import: every chosen file is the same as on this computer.",
            TransferImportProblem.CaseOnlyConflict => $"{Name} differs from a file on this computer only in upper and lower case, so this settings package can't be imported.",
            TransferImportProblem.ReceiverUnreadable => $"{Name} on this computer can't be read (or is not a file Campertron replaces), so the import can't replace it.",
            TransferImportProblem.ReceiverUnloadable => $"{Name} on this computer has settings Campertron can't read, so the import can't replace it. Fix or remove it first.",
            TransferImportProblem.ReceiverNotReplaceable => $"{Name} on this computer can't be replaced by the import.",
            TransferImportProblem.PackageFileUnloadable => $"{Name} in this settings package has settings Campertron can't read, so it can't be imported.",
            TransferImportProblem.PackageFileRefused => $"{Name} can't be imported as it would be: {Detail}",
            TransferImportProblem.PackageFileNotEditable => $"Campertron can't put this computer's own settings into {Name} without changing the rest of it, so it can't be imported.",
            TransferImportProblem.PackageFileHoldsSecret => $"{Name} in this settings package holds a password, token or topic outside its setting (for example in a comment), so it can't be imported.",
            TransferImportProblem.BackupNotSecretFree => $"Campertron can't keep a backup of {Name} without its password or topic, so the import can't replace it.",
            _ => throw new ArgumentException("Not a preview's problem.", nameof(Problem))
        }, Name);

        // ---- an apply's results ----

        internal static TransferImportFailure Apply(TransferImportProblem Problem) => new TransferImportFailure(Problem, Problem switch
        {
            TransferImportProblem.NotApplicable => "This preview can't be applied; nothing was imported.",
            TransferImportProblem.NotConfirmed => "The confirmation is not for this preview; nothing was imported.",
            TransferImportProblem.AlreadyUsed => "This preview was already applied or tried; look at the preview again.",
            TransferImportProblem.Cancelled => "The import was cancelled; nothing was imported.",
            TransferImportProblem.UnsavedChanges => "Save or discard your unsaved changes to the settings or search files first; nothing was imported.",
            TransferImportProblem.RefreshRunning => "The campground data is being refreshed; import when the refresh has ended. Nothing was imported.",
            TransferImportProblem.MonitoringStarting => "The monitoring is starting; import when it has started. Nothing was imported.",
            TransferImportProblem.LockHeld => "Another import is being applied to this settings folder; nothing was imported.",
            TransferImportProblem.LockUnavailable => "Campertron can't take the import's lock in the settings folder (it may not be writable); nothing was imported.",
            TransferImportProblem.Stale => "The settings changed since this preview was made; look at the preview again. Nothing was imported.",
            TransferImportProblem.FailedNothingChanged => "The import failed, and nothing was changed: the settings, search and secret files are as they were.",
            _ => throw new ArgumentException("Not an apply's problem.", nameof(Problem))
        });

        internal const string Applied = "The import was applied.";

        // the apply failed and not everything was restored: by names only, never a path or a value
        internal static TransferImportFailure NotRestored(IReadOnlyList<string> Restored, IReadOnlyList<string> NotRestored) => new TransferImportFailure(TransferImportProblem.FailedNotRestored,
            "The import failed, and not everything could be put back. "
            + (Restored.Count > 0 ? "As before: " + string.Join(", ", Restored) + ". " : string.Empty)
            + "Not restored: " + string.Join(", ", NotRestored) + ". "
            + "The backup in the settings folder (" + TransferBackupSet.FolderName + ") holds the earlier settings without their passwords and topic, so enter them again if you restore a file from it. Campertron does nothing more about it by itself.");

        // a new secret file's logical name in a result: its secret's kind, never its file name or path
        internal static string SecretFileWords(TransferSecretKind Secret) => Secret == TransferSecretKind.SmtpPassword ? "the new file for the SMTP password" : "the new file for ntfy's access token";

        internal const string CleanupWarning = "An earlier secret file of Campertron's was left in the settings folder, because Campertron could not prove that nothing needs it any more; the import itself was applied.";

        // ---- a preview's words ----

        internal static string PackageWords(TransferPackage Package)
        {
            string Made = $"This settings package was made by Campertron {Package.ExportedBy} (package format {Package.FormatVersion}).";
            return Package.Encrypted
                ? Made + " It was encrypted with a passphrase."
                : Made + " It is not encrypted: its checksums detect damage only, not a change someone made on purpose.";
        }

        internal static string Created(string Name) => $"{Name} will be created.";
        internal static string Replaced(string Name) => $"{Name} will be replaced.";
        internal static string Unchanged(string Name) => $"{Name} is the same as on this computer and will not be written.";
        internal static string LeftOut(string Name) => $"{Name} is left out of the import; this computer's is not changed.";

        internal const string OthersNotTouched = "This computer's other search files are not changed, and the import deletes nothing.";

        internal static string Kept(string File, string Key) => $"{Key} in {File} is kept from this computer.";

        internal static string SecretImported(TransferSecretKind Secret) => Secret switch
        {
            TransferSecretKind.SmtpPassword => "The SMTP password will be imported into a new file of Campertron's that only you can read, and Email.yaml's smtpPasswordFile will name it; Email.yaml's plain-text smtpPassword is never written.",
            TransferSecretKind.NtfyToken => "ntfy's access token will be imported into a new file of Campertron's that only you can read, and Ntfy.yaml's tokenFile will name it.",
            _ => "ntfy's topic will be imported into Ntfy.yaml, which only you can read."
        };

        // the change of a secret's source kind: never a path or a value
        internal static string SourceChange(TransferSecretKind Secret, string Before) =>
            $"{Capitalized(TransferWording.SecretWords(Secret))} then comes from that new file instead of {Before}.";

        internal static string SourceBefore(TransferImportSecretSource Source, TransferSecretKind Secret) => Source switch
        {
            TransferImportSecretSource.File => Secret == TransferSecretKind.SmtpPassword ? "the file smtpPasswordFile named" : "the file tokenFile named",
            TransferImportSecretSource.VariableOrPlainText => "Email.yaml's plain-text smtpPassword (or " + SmtpPassword.EnvironmentVariable + ", where it is set)",
            TransferImportSecretSource.VariableOrNone => (Secret == TransferSecretKind.SmtpPassword ? SmtpPassword.EnvironmentVariable : NtfyToken.EnvironmentVariable) + ", where it is set",
            _ => "Ntfy.yaml's topic"
        };

        internal const string PathIsThisComputers = "A new secret file is named by its path on this computer: a Campertron that uses this settings folder through another path (in a container, for example) does not find it there.";

        internal static string SecretNotImported(TransferSecretKind Secret, TransferImportSecretDecision Decision) => Capitalized(TransferWording.SecretWords(Secret)) + Decision switch
        {
            TransferImportSecretDecision.LeftOut => " is left out of the import, as chosen; this computer's own stays as it is.",
            TransferImportSecretDecision.FileLeftOut => $" is not imported, because {(Secret == TransferSecretKind.SmtpPassword ? ConfigFiles.EmailFileName : ConfigFiles.NtfyFileName)} is left out of the import.",
            TransferImportSecretDecision.RefusedReadableByOthers => " is not imported: other users of this computer could read Ntfy.yaml. This computer's own topic stays as it is.",
            TransferImportSecretDecision.RefusedUnusable => Secret == TransferSecretKind.NtfyTopic
                ? " is not imported: it is not a topic ntfy accepts. This computer's own topic stays as it is."
                : " is not imported: it holds characters a token can't have. This computer's own setting stays as it is.",
            _ => " is not imported."
        };

        internal const string NtfyIncomplete = "ntfy stays incomplete after the import: Ntfy.yaml has no topic, so nothing is published to ntfy until you enter one.";

        internal static string OutputToResult(OutputType Output) => Output == OutputType.Email || Output == OutputType.Ntfy
            ? $"outputTo will be {Output}. With it, CampertronConsole started without arguments, a container that starts it so, and the web UI at its start monitor by themselves; the desktop app and the interactive console monitor only after you select Start."
            : $"outputTo will be {Output}. With it, no start of Campertron monitors.";

        internal static string OutputToExcluded(OutputType Package, OutputType Kept, IEnumerable<ConfigDiagnostic> Findings) =>
            $"outputTo's change to {Package} can't be applied, because the monitoring would refuse to start with it: {Findings_(Findings)} The rest of this settings package can be imported, and outputTo stays {Kept}.";

        internal static string OutputToWarning(OutputType Output, IEnumerable<ConfigDiagnostic> Findings) =>
            $"With outputTo: {Output}, the monitoring would refuse to start after the import: {Findings_(Findings)}";

        private static string Findings_(IEnumerable<ConfigDiagnostic> Findings) => string.Join(" ", Findings.Select(D => D.FileName + ": " + D.Message).Distinct());

        internal static readonly IReadOnlyList<string> RestartWords = new[]
        {
            "An import changes saved files only, so nothing has to restart. The editors show the saved files again afterwards.",
            "A monitoring that is running keeps the settings it started with and reads the new ones when it next starts; the import starts, stops and restarts no monitoring.",
            "The web UI decides at its own start whether it monitors, so a changed outputTo takes effect when it next starts."
        };

        internal const string BackupWords = "Before anything is written, Campertron keeps a backup of every file the import replaces, without passwords or topic, in the settings folder (" + TransferBackupSet.FolderName + "); nothing reads it.";

        internal static string Validation(ConfigDiagnostic Finding) => Finding.FileName + ": " + Finding.Message;

        private static string Capitalized(string Words) => char.ToUpperInvariant(Words[0]) + Words.Substring(1);
    }
}
